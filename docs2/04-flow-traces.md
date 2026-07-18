# 04 — Flow Traces

Goal: trace five flows end to end — UI → HTTP → service → domain → database → side effects —
including the behavior most readers miss. Tracing is the core mid-level skill: when you can
hold a whole flow in your head, debugging stops being archaeology.

Format per flow: the trace, then **What most readers miss**, then a drill.

---

## Flow 1: Hosted Login

| # | Layer | Code | What happens |
| --- | --- | --- | --- |
| 1 | UI | [`LoginPage.tsx:46-53`](../frontend/src/pages/LoginPage.tsx#L46-L53) | react-hook-form + zod validates tenant slug, email, password (min 8), optional TOTP |
| 2 | UI | [`client.ts:82-83`](../frontend/src/api/client.ts#L82-L83) | `POST /api/v1/auth/login` JSON |
| 3 | API | [`AuthController.Login`](../src/NetAuth0.Api/Controllers/AuthController.cs#L22-L27) | public endpoint, `public-auth` rate limit (20/min/IP) |
| 4 | App | [`IdentityService.LoginAsync`](../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95) | tenant → user → lockout → password → MFA → tokens; audit at each branch |
| 5 | Infra | [`JwtTokenIssuer.IssueUserTokensAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53) | queries user's permissions for the dashboard app, signs access + ID tokens |
| 6 | UI | [`LoginPage.tsx:28-38`](../frontend/src/pages/LoginPage.tsx#L28-L38) | stores token + tenant ID in `localStorage`, navigates to dashboard |

**What most readers miss:**

- **Failure-order is an enumeration defense.** Unknown tenant and unknown user both throw
  the same "Invalid credentials." ([`IdentityService.cs:51-56`](../src/NetAuth0.Application/Identity/IdentityService.cs#L51-L56)) —
  the response doesn't reveal whether an email exists. But the *timing* differs: an unknown
  user skips BCrypt verification (~100ms at work factor 12). A measurable user-enumeration
  oracle survives the message hygiene.
- **Lockout counts only password failures.** A wrong TOTP code throws at
  [line 80-84](../src/NetAuth0.Application/Identity/IdentityService.cs#L80-L84) *without*
  calling `RegisterFailure`. The 6-digit code space is brute-forceable at 20 req/min/IP if
  the attacker has the password. (Doc 08, finding F3.)
- **The MFA "ticket" is theater.** When MFA is required, a `loginTicket` is generated
  ([line 75](../src/NetAuth0.Application/Identity/IdentityService.cs#L75)) and returned —
  but never stored, and the completion path never asks for it. Finishing MFA = re-POSTing
  the full credentials with `totpCode`. The field exists for a flow that was never built.
- **First-party login bypasses OAuth.** The service looks up the hardcoded client
  `netauth0_admin_spa` ([line 89](../src/NetAuth0.Application/Identity/IdentityService.cs#L89))
  and issues tokens directly — no authorize redirect, no PKCE, no refresh token (notice the
  dashboard session can't refresh; it just expires after 15 minutes).

**Drill:** the seeded admin enables MFA, then submits a correct password and an empty
`totpCode` five times in a row. What does each request return, and is the account locked
afterwards? Verify against [lines 58-84](../src/NetAuth0.Application/Identity/IdentityService.cs#L58-L84).
*(Answer: five `requiresMfa=true` responses; nothing increments `FailedAccessCount`; not locked.)*

---

## Flow 2: Authorization Code + PKCE (external client)

| # | Layer | Code | What happens |
| --- | --- | --- | --- |
| 1 | Client | — | builds `/connect/authorize?client_id=…&redirect_uri=…&response_type=code&scope=…&state=…&code_challenge=…&code_challenge_method=S256&user_id=…` |
| 2 | API | [`OAuthController.Authorize`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L11-L32) | binds query params; `[ApiController]` 400s missing required ones |
| 3 | App | [`CreateAuthorizationCodeAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L25-L69) | client/grant/redirect/PKCE checks; blocked-user check; mints hashed 5-min code |
| 4 | API | 302 | `Location: {redirect_uri}?code=…&state=…` |
| 5 | Client | [`CallbackPage.tsx`](../frontend/src/pages/CallbackPage.tsx) | (demo) reads `code` + `state` from query |
| 6 | Client | — | `POST /connect/token` form: `grant_type=authorization_code, code, redirect_uri, client_id, code_verifier` |
| 7 | App | [`ExchangeAuthorizationCodeAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L107-L153) | the seven-check gauntlet (doc 03), consume code, issue JWTs + refresh token |

**What most readers miss:**

- **Two `SaveChangesAsync` per exchange** (lines 149, plus audit's own save): code
  consumption and refresh-token insert commit together at line 149 — *that part* is atomic.
  The audit write afterwards is a separate commit.
- **Scope is stored, echoed, and never interpreted.** Nothing validates the requested scope
  against anything; `openid profile email garbage` flows straight through to the token.
- **The non-PKCE path is unreachable.** `code_challenge` binds as a required non-nullable
  string at the controller, and the exchange requires `code_verifier` unconditionally
  ([line 109](../src/NetAuth0.Application/OAuth/OAuthService.cs#L109)). A client with
  `RequirePkce=false` still can't skip PKCE end to end. Harmless today; surprising when
  someone "turns PKCE off" and nothing changes.

**Drill:** enumerate every input that, if tampered with between step 4 and step 6, causes
the exchange to fail — and the exact line that catches each.

---

## Flow 3: Refresh Rotation & Replay

| # | Layer | Code | What happens |
| --- | --- | --- | --- |
| 1 | Client | — | `POST /connect/token` `grant_type=refresh_token&refresh_token=…&client_id=…` |
| 2 | App | [`ExchangeRefreshTokenAsync:188-190`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L188-L190) | SHA-256 the presented token, lookup by hash **scoped to this client** |
| 3 | App | [lines 192-197](../src/NetAuth0.Application/OAuth/OAuthService.cs#L192-L197) | expired → quiet revoke, error (no family revocation) |
| 4 | App | [lines 199-203](../src/NetAuth0.Application/OAuth/OAuthService.cs#L199-L203) | dead-but-unexpired → **family revocation** + `reuse_detected` audit |
| 5 | App | [lines 209-228](../src/NetAuth0.Application/OAuth/OAuthService.cs#L209-L228) | insert child token (same family), save, mark old `RotateTo(child)`, issue JWTs, save |
| 6 | Domain | [`RefreshToken`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L239-L278) | the family bookkeeping: `FamilyId`, `RotatedFromId`, `ReplacedById`, `ReuseDetectedAt` |

**What most readers miss:**

- **The save-ordering hazard.** Child saved (211) → old marked rotated in memory (212) →
  token issuance (217, which can itself save — key bootstrap!) → final save (227). A crash
  at 217 leaves *two active tokens in one family* and the caller got neither. Replay
  detection still eventually catches double-use, but the invariant "≤1 active token per
  family" is unenforced.
- **Client-secret check is conditional on the client *sending* one**
  ([lines 183-186](../src/NetAuth0.Application/OAuth/OAuthService.cs#L183-L186)): a
  confidential client's refresh token can be redeemed *without* the secret if the attacker
  just omits it. Compare with the unconditional check in the code exchange (line 115).
  (Doc 08, finding F5.)
- **Client-credentials refresh exists.** `UserId` is nullable; the M2M path gets refresh
  tokens too if granted ([lines 220-225](../src/NetAuth0.Application/OAuth/OAuthService.cs#L220-L225)) —
  unusual (most providers don't, since the client can just re-authenticate).

**Drill:** write the state table of a family across: login, refresh, refresh, replay of
token #1, attempt with token #3. Columns: token, `ReplacedById`, `RevokedAt`,
`ReuseDetectedAt`. Then check yourself against `IsActive` and `MarkReuse`.

---

## Flow 4: Admin Registers an OAuth Client

| # | Layer | Code | What happens |
| --- | --- | --- | --- |
| 1 | UI | [`ApplicationsPage.tsx:38-48`](../frontend/src/pages/ApplicationsPage.tsx#L38-L48) | zod-validated form; comma-split redirect URIs and grants; mutation |
| 2 | API | [`AdminController.CreateApplication`](../src/NetAuth0.Api/Controllers/AdminController.cs#L20-L25) | `[Authorize(Policy="applications:manage")]` |
| 3 | App | [`AdminService.CreateApplicationAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L28-L54) | server generates `na0_…` client ID; secret only for non-SPA; BCrypt-hashes it; PKCE forced on for non-M2M |
| 4 | DB | [`AppDbContext.cs:36-57`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L36-L57) | unique client ID; unique (app, uri) and (app, grant) |
| 5 | UI | [`ApplicationsPage.tsx:44-47`](../frontend/src/pages/ApplicationsPage.tsx#L44-L47) | shows the secret **once**, invalidates `['apps', tenantId]` |

**What most readers miss:**

- **The secret round-trip is the only time it exists in plaintext** outside the client's
  hands. The list endpoint never returns secrets — by construction
  ([`ApplicationDto`](../src/NetAuth0.Application/Admin/AdminContracts.cs#L15) has no secret
  field). Contract design *is* security design.
- **`tenantId` comes from the request, not the token.** The caller's JWT carries a
  `tenant_id` claim; nothing compares them. Any holder of `applications:manage` can create
  apps in *any* tenant ([`AdminService.cs:30-33`](../src/NetAuth0.Application/Admin/AdminService.cs#L30-L33)
  checks existence only). The flagship tenant-isolation gap (doc 08, finding F2).
- **Redirect URIs are not validated as URLs.** `"not a url"` is accepted. The runtime
  exact-match means it can never *match* anything dangerous accidentally, but garbage
  registrations and `http://` production callbacks pass silently.
- **Request lifetimes are partially ignored:** access-token lifetime is hardcoded to 15
  ([`AdminService.cs:38`](../src/NetAuth0.Application/Admin/AdminService.cs#L38)) even
  though the request record carries no field for it — but `RefreshTokenLifetimeDays` *is*
  honored, clamped 1–365.

**Drill:** the UI's zod schema and the server's checks overlap but aren't identical. List
every validation that exists on exactly one side, and which side. Why must the server side
be the superset?

---

## Flow 5: Key Rotation, End to End

| # | Layer | Code | What happens |
| --- | --- | --- | --- |
| 1 | Admin | `POST /api/v1/admin/signing-keys/rotate` ([`AdminController.cs:72-77`](../src/NetAuth0.Api/Controllers/AdminController.cs#L72-L77)) | requires `applications:manage` |
| 2 | Infra | [`KeyManagementService.RotateAsync`](../src/NetAuth0.Infrastructure/Security/KeyManagementService.cs#L10-L23) | retire all unretired keys; create RSA-2048; save |
| 3 | Issue | [`GetCurrentSigningKeyAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L112-L117) | next token signs with the new key (newest unretired by `CreatedAt`) |
| 4 | Verify | [`GetJwksAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L68-L92) | JWKS = unretired keys + keys retired <7 days; old tokens keep validating |

**What most readers miss:**

- **No audit event.** Every other admin action writes one; rotating the keys that all trust
  derives from doesn't. (Doc 10, ticket 1 — it's the ideal first PR.)
- **Ordering by `CreatedAt` with second-ish precision** — two keys created in the same
  instant make "current" ambiguous. Unlikely; cheap to harden.
- **JWKS rebuilds RSA objects from private PEM on every request** — correctness fine,
  performance and key-handling hygiene questionable (doc 09).

**Drill:** write the no-downtime rotation runbook for a fleet of resource servers that
cache JWKS for 10 minutes: what's the minimum safe interval between "rotate" and "delete old
key row," in terms of token lifetime and cache TTL?

---

## Cross-Flow Invariants

When you change *anything* in these flows, re-verify these five statements. They're the
repo's real contract:

1. No bearer secret (code, refresh token, recovery code) is ever persisted in plaintext.
2. An authorization code is redeemable at most once, only by its client, only with its
   redirect URI and verifier, only within 5 minutes.
3. At most one refresh token per family is active at a time *(currently aspirational —
   see Flow 3)*.
4. Every security-relevant state change writes an audit event *(currently false for key
   rotation)*.
5. Permission changes take effect within one access-token lifetime (15 min).

Knowing which invariants are aspirational versus enforced is exactly the kind of judgment
that distinguishes a senior review from a junior one.
