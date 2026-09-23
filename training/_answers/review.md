# Sealed answer — Blind Code Review (`training/review/EXERCISE.md`)

Do not open this until you've written your own findings list.

## Source

`docs2/08-security-review-walkthrough.md` already contains a verified, evidence-cited
findings table (F1–F14) for this exact commit (`1d70eac`). This file does not re-derive
those findings from scratch — it re-ranks them against the master rubric (correctness,
security, data integrity, concurrency, failure handling, performance, operability, tests,
maintainability), adds ASVS chapter tags since this batch's review is security-emphasized,
and flags which are AI-generated-code smells. Verify each citation yourself; two were spot
checked directly for this file (`OAuthController.cs:22`, `frontend/src/api/client.ts:50-52`)
and match.

## Ranked findings (production severity, security-weighted)

| Rank | Finding | Evidence | Category | ASVS | AI-smell? |
|---|---|---|---|---|---|
| 1 | **Authorize endpoint trusts `user_id` as a raw query parameter — no session, no consent.** Anyone who can reach `/connect/authorize` and knows (or guesses/enumerates) a user's GUID gets a valid authorization code for that user, for any client. This is a full account-takeover primitive, not a hardening gap. | `OAuthController.cs:22` (`[FromQuery(Name = "user_id")] Guid userId`) | Correctness / Security | ASVS V4 (access control), V11 (business logic) | **Yes** — scaffolded to "look like" OAuth without the actual authentication step; the kind of gap that survives because the happy-path tests never call it without a pre-known `userId` |
| 2 | **No tenant isolation on admin APIs; `ValidateAudience=false` removes the last real gate.** `AdminController`/`AdminService` trust `tenantId` from the query string, never compare it to the caller's token claim. Any principal holding `applications:manage`/`users:manage` for *any* tenant can read/mutate every other tenant, and because audience validation is off, a token minted for one client works as a bearer for all of them. | `AdminController.cs:15-70`, `AdminService.cs:30-33`, `Program.cs:60` | Security / Data integrity | ASVS V4, V13.1 (multi-tenant boundary) | No — real design gap, not scaffolding |
| 3 | **TOTP has no per-account brute-force counter.** Password lockout (`RegisterFailure`, 5 attempts/15 min) exists and is exercised; the MFA step next to it has nothing equivalent — only the shared 20/min IP rate limiter stands between a password holder and a 6-digit code. | `IdentityService.cs:80-84` vs `IdentityService.cs:58-70` (compare the two) | Security | ASVS V2.2 (anti-automation on OTP) | **Yes** — classic half-implemented control: the pattern exists one function away and wasn't copied over |
| 4 | **Refresh-token rotation isn't atomic.** The replacement token is added and `SaveChangesAsync`'d, *then* `existing.RotateTo(...)` marks the old one consumed, with a token-issuance call (which can itself write, on first-run key bootstrap) sitting between the two saves. A crash or timeout in that window leaves two live tokens in one family — the exact "at most one active token per family" invariant the reuse-detection logic assumes. | `OAuthService.cs:209-228` | Concurrency / Data integrity | ASVS V3.3 (token invalidation) | No |
| 5 | **Confidential clients can call `refresh_token` without their client secret.** `ValidateClientSecretIfRequiredAsync` for the refresh grant only runs `if (!string.IsNullOrWhiteSpace(request.ClientSecret))` — omit the field and the check is skipped entirely, unlike every other grant, which requires it unconditionally. A stolen refresh token plus a public `client_id` is enough. | `OAuthService.cs:183-186` (contrast with line 115 in the auth-code path) | Security | ASVS V6.2, V13.2 | **Yes** — inconsistency between two structurally identical code paths is the textbook "one of these was copy-pasted and simplified wrong" smell |
| 6 | **Recovery codes are generated, hashed, and stored — and never redeemable.** `BeginTotpEnrollmentAsync` creates 10 recovery codes; `RecoveryCode.Use(...)` exists on the domain entity but has zero callers anywhere in `IdentityService` or elsewhere. A user who loses their authenticator is permanently locked out despite the API advertising recovery. | `IdentityService.cs:104-111`; grep `RecoveryCode` — only writer, no reader | Availability / Correctness | — | **Yes** — feature that's "presented as working" (returned to the client, documented) but has no back half; exactly the smell the batch brief asks to flag |
| 7 | **Signing keys are stored as plaintext PEM in Postgres.** `SigningKey.PrivateKeyPem` is a plain column; DB read access (a leaked connection string, a misconfigured replica, a backup dump) is enough to forge tokens for any tenant, silently, with no detection. Key rotation itself (`AdminController.cs` `signing-keys/rotate`) writes no audit event either. | `IdentityEntities.cs:283-297`, `KeyManagementService.cs`, `AuditWriter.cs` (absence of a rotation event) | Data protection / Operability | ASVS V6.4 (key management) | No — real architectural gap for a lab-scale project, correctly flagged as "fix before productionizing" rather than launch-blocking |
| 8 | **MFA enrollment verification is an IDOR.** `VerifyTotpEnrollmentAsync` looks up the enrollment by `EnrollmentId` alone; nothing ties it to the calling user's identity even though the endpoint is `[Authorize]`. | `IdentityService.cs:114-124` | Security | ASVS V4.2 | No |
| 9 | **Bearer tokens in `localStorage`.** `frontend/src/api/client.ts:50-64` persists the access token in `localStorage`; any XSS anywhere in the SPA is a full token theft with no HttpOnly/SameSite boundary. 15-minute expiry is the only mitigation. Acceptable for a lab SPA; flagged because the review scope explicitly includes the frontend. | `client.ts:50-64` (spot-checked — confirmed) | Security | ASVS V3.4 (cookie/token storage) | No |
| 10 | **Every failure collapses to HTTP 400 with the raw exception message in Development**, and OAuth errors aren't RFC 6749-shaped (`invalid_grant` vs `invalid_client` vs `invalid_request` are indistinguishable to a client library). | `Program.cs:113-123` | Failure handling / Operability | ASVS V7.4 (error handling) | No |
| 11 | **Login has a user-enumeration timing oracle.** Unknown-tenant/unknown-user paths return the same "Invalid credentials" message but skip the ~100ms BCrypt verify that a known-user wrong-password path pays, so response latency alone distinguishes the two. | `IdentityService.cs:51-64` | Security | ASVS V2.1 | No |
| 12 | **Rate limiter partitions on raw `RemoteIpAddress` with no `ForwardedHeaders` configuration.** Behind any reverse proxy or LB, every real client shares one IP and therefore one 20/min bucket — a trivial DoS of the login/token endpoints for all tenants. Without a proxy, the limiter is defeated by IP rotation. | `Program.cs:73-83` | Availability / Operability | ASVS V13.4 | No |
| 13 | **`code_challenge_method=plain` is still accepted, `scope` is never validated against what the client is allowed to request, `state` has no length bound.** Spec-legal (RFC 7636 permits `plain`), but RFC 9700 (OAuth Security BCP) says S256-only for anything issuing this shape of token. | `OAuthService.cs:256-267` | Security (hardening) | ASVS V13.2 | No |
| 14 | **`Database.EnsureCreatedAsync()` fallback runs whenever there are no migrations.** There's a `DesignTimeDbContextFactory` for `dotnet ef migrations add`, but none are committed, so every fresh environment silently schema-creates instead of migrating — the first real migration written against a live database will have no baseline to diff from. | `Program.cs:138-150`; `Persistence/` has no `Migrations/` folder | Operability | — | **Yes** — "testing strategy" is documented but there's no committed migration history to test against |

## AI-generated-code smells specifically (batch brief callout)

- Rank 1 (unauthenticated `user_id` param): a control that *looks* like OAuth (state, PKCE,
  redirect_uri validation are all present and mostly correct) but is missing the one step
  that makes it authentication rather than a parameter the caller supplies.
- Rank 3 (MFA has no lockout) and rank 5 (refresh grant skips secret check when omitted):
  both are "the sibling code path did it right, this one didn't" — the single highest-signal
  smell for spotting where an LLM wrote two similar functions independently instead of
  sharing one guarded code path.
- Rank 6 (recovery codes never redeemable): a feature whose write side is fully built,
  returned to the API caller as if functional, and has no read side. This is the sharpest
  "presented as working but isn't" instance in the codebase — call it out explicitly if this
  project is ever shown as a portfolio piece.
- Rank 14 (no committed migrations despite a migration factory existing): docs claim a
  schema-evolution story that the repo doesn't actually have yet.

## What a staff reviewer would NOT flag as high severity here

- `RequireHttpsMetadata = false` (`Program.cs:55`) — correct for local/dev, not a finding by
  itself; only worth a note if a deployment doc claimed HTTPS enforcement (it doesn't).
- Passwords hashed with BCrypt work factor 12 (`SecurityServices.cs:15`) — reasonable.
- `FixedTimeEquals` used for PKCE verifier comparison (`SecurityServices.cs:29-34`) — correct
  instinct, correctly implemented.
- PKCE is required by default per client (`RequirePkce = true`) and enforced consistently at
  both authorize- and token-time — this part of the OAuth implementation is solid; don't
  spend review time relitigating it.

## Where the two incidents fit

Neither of this repo's two committed incidents (`training/incidents/001-*`,
`training/incidents/002-*`) is on this list — they're regressions introduced on top of code
that *passed* this review (`ValidateRedirectUri`'s exact-match compare and the absence of an
explicit `ClockSkew` were both fine on `main`). That's deliberate: a security review catches
what's wrong today; incidents test whether you can diagnose a *new* regression a plausible
follow-up PR introduced into code that used to be correct.
