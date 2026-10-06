# 03 — OAuth2 & OIDC, Learned From This Code

Goal: after this doc you can explain OAuth2/OIDC, PKCE, JWTs, refresh rotation, and key
rotation **with code citations instead of definitions**. This is the highest-leverage
knowledge in the repo — auth concepts transfer to every job; this codebase happens to
implement them readably.

## The Problem OAuth Solves

An app ("NetLinear") wants to know who you are without ever seeing your password. The
identity provider (this repo) authenticates you and hands NetLinear *tokens* — scoped,
expiring, revocable proofs. Three flows exist here, gated per client by
[`GrantType`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L12-L17) rows:

| Flow | For | Entry point |
| --- | --- | --- |
| Authorization Code + PKCE | apps acting for a user (SPA, web app) | `GET /connect/authorize` → `POST /connect/token` |
| Client Credentials | machine-to-machine, no user | `POST /connect/token` |
| Refresh Token | extending a session without re-login | `POST /connect/token` |

Clients discover everything from one URL —
[`GET /.well-known/openid-configuration`](../src/NetAuth0.Api/Controllers/OidcController.cs#L9-L27)
— which is how off-the-shelf OAuth libraries configure themselves against any provider.

## Stage 1: The Authorization Request

[`OAuthService.CreateAuthorizationCodeAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L25-L69)
performs, in order:

1. **`response_type=code` only** (line 27) — the legacy implicit flow (`token`) is
   deliberately unsupported; it returned tokens in URL fragments where they leak via
   browser history and Referer headers.
2. **Client lookup + grant check**
   ([`GetClientAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L232-L246)) —
   unknown client or disallowed grant fails *before* anything user-related happens.
3. **Exact redirect URI match**
   ([`ValidateRedirectUri`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L248-L254)) —
   `StringComparison.Ordinal`, no prefix matching, no wildcard. This single check is what
   stops an attacker from receiving your authorization code at `https://evil.example`.
   Prefix/wildcard matching is a well-known source of real OAuth vulnerabilities (RFC 9700
   requires exact string matching); `training/incidents/002` is a drill built on exactly
   that regression.
4. **PKCE presence check** ([`ValidatePkce`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L256-L267)) — see below.
5. **Code minting** (lines 42-56): 32 random bytes, base64url
   ([`SecretGenerator.UrlSafe`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L21)),
   stored as a **SHA-256 hash** with a **5-minute expiry**, single-use via `ConsumedAt`
   ([`AuthorizationCode.IsActive`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L235)).
6. **Redirect back** with `code` and the client's `state` echoed verbatim (lines 59-66).
   `state` is the *client's* CSRF defense — the provider just reflects it; the client must
   verify it matches what it sent. ([`CallbackPage.tsx`](../frontend/src/pages/CallbackPage.tsx)
   shows the receiving side, though it stops short of the exchange.)

> **Big asterisk:** the endpoint takes `user_id` as a query parameter
> ([`OAuthController.cs:22`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L22)).
> A real provider binds the authorize request to an **authenticated browser session** —
> you can only mint codes for yourself, after the provider's own login + consent UI. Here,
> anyone who can reach the endpoint can request a code for any user ID. It's the repo's
> biggest known simplification; doc 08 rates it.

## Stage 2: PKCE — Why a Public Client Can Be Trusted

A SPA can't keep a secret (its source ships to the browser). So how do we know the thing
exchanging the code is the thing that started the flow? **Proof Key for Code Exchange:**

1. Client invents a random `code_verifier`, sends `code_challenge = SHA256(verifier)` with
   the authorize request. The challenge is stored on the code row
   ([`IdentityEntities.cs:230-231`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L230-L231)).
2. At exchange time the client presents the raw `code_verifier`. The server re-hashes and
   compares ([`OAuthService.cs:133-143`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L133-L143)).

An attacker who steals the *code* (from a log, a referer, a malicious redirect-uri toehold)
still can't redeem it without the verifier, which never left the legitimate client.

Note the comparison uses
[`FixedTimeEquals`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L29-L34) —
constant-time, so timing differences don't leak how many leading characters matched. The
repo accepts `plain` as a challenge method (verifier sent as its own challenge), which the
RFC permits but modern policy rejects: with `plain`, stealing the challenge *is* stealing
the verifier. Tightening this is doc 10, ticket 4.

## Stage 3: The Token Exchange

[`ExchangeAuthorizationCodeAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L107-L153)
is a checklist — every line is a distinct attack it forecloses:

| Check | Line | Attack stopped |
| --- | --- | --- |
| client exists + grant allowed | 114 | confused-deputy via wrong client |
| client secret verified (confidential clients) | 115 | client impersonation |
| code hash found **and** belongs to this client | 118-121 | code minted for client A redeemed by client B |
| code not expired, not consumed | 123-126 | replay; stolen-code shelf life |
| `redirect_uri` equals the one used at authorize | 128-131 | code interception via redirect switch |
| PKCE verifier matches | 133-143 | code theft without verifier |
| code marked consumed | 145 | second redemption — *sequential* only |

That last row has a caveat. "Consumed" is a property set in memory and saved at line 149;
`AuthorizationCode` has no concurrency token
([`AppDbContext.cs:92-99`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L92-L99)),
so two requests that load the same unconsumed code at the same moment both pass line 123 and
both get tokens. Single-use holds for replays that arrive *after* the first exchange
committed, not for parallel ones. Doc 06, exercise 11.

Client authentication accepts both HTTP Basic and form fields
([`ReadClientCredentials`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L61-L72)), per
RFC 6749 §2.3.1. Client secrets are BCrypt-hashed like passwords
([`AdminService.cs:37`](../src/NetAuth0.Application/Admin/AdminService.cs#L37)) — the DB
never holds a usable secret.

The response ([`OAuthController.cs:50-58`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L50-L58))
is the standard shape: `access_token`, `id_token`, `refresh_token`, `expires_in`, `scope`.

**Access token vs ID token** — the distinction interviews love:

- The **access token** is *for the API* (authorization). Here it carries `sub`, `tenant_id`,
  `client_id`, `scope`, and one `permissions` claim per permission
  ([`JwtTokenIssuer.cs:26-34`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L26-L34)).
- The **ID token** is *for the client* (authentication — "who logged in"). It carries
  `email`, `name`, and the `nonce` the client sent
  ([`JwtTokenIssuer.cs:36-48`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L36-L48)).
  The nonce binds the ID token to the client's original request — the ID-token analog of
  `state`.

## Stage 4: Refresh Token Rotation — the Crown Jewel

Refresh tokens live for days ([30 by default](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L59))
and mint new access tokens. A stolen one is a stolen session. The defense implemented in
[`ExchangeRefreshTokenAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L175-L230):

**Rotation:** every use kills the presented token and issues a child
([`RotateTo`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L267-L271)), linked by
`RotatedFromId`/`ReplacedById` and sharing a `FamilyId`.

**Replay detection:** if a token arrives that is dead (`ReplacedById` set, or revoked —
see [`IsActive`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L266)), someone is
holding a stale copy. You can't tell whether it's the attacker or the victim presenting it —
so you don't have to: [`RevokeFamilyForReuseAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L300-L310)
kills **every token in the family** and writes a `refresh_token.reuse_detected` audit event.
Legitimate user logs in again; attacker is ejected. This is exactly the scheme Auth0 and
modern OIDC guidance recommend.

Walk the timeline:

```
login ──► RT1 (family F)
use RT1 ─► RT1 dead, RT2 issued (family F)          ← normal rotation
use RT2 ─► RT2 dead, RT3 issued (family F)
use RT1 ─► RT1 is dead ⇒ REUSE ⇒ RT1,RT2,RT3 all marked reused+revoked
use RT3 ─► fails (family dead) — attacker holding RT3 gets nothing
```

Four sharp edges you should be able to discuss (2-4 are gym exercises in doc 06):

1. **Expiry is checked before reuse** (lines 192-197): an *expired* token is revoked quietly
   without family revocation. Defensible — expiry isn't evidence of theft — but it means a
   replayed-but-expired stolen token never trips the alarm.
2. **Rotation is not atomic.** The replacement is saved (line 211) *before* the old token is
   marked rotated (line 212, saved at line 227), with token issuance in between. A crash in
   the middle leaves two live tokens in one family. Doc 08 rates this; doc 07 shows the test
   that would pin the behavior.
3. **Replay detection assumes requests arrive one at a time.** Two requests presenting the
   same live token in parallel both see `IsActive == true` at line 199, both mint a child,
   and neither trips `RevokeFamilyForReuseAsync`. Nothing in the schema stops it
   (`RefreshToken` has no concurrency token). The family ends up with two live branches and
   no `reuse_detected` event. Doc 06, exercise 11.
4. **The refresh path never re-checks the user.** Lines 205-207 load the `User` but never
   look at `IsBlocked`, while authorize (line 37) and login
   ([`IdentityService.cs:58`](../src/NetAuth0.Application/Identity/IdentityService.cs#L58))
   both do. Doc 06, exercise 12.

Sessions in the UI are just refresh-token families:
[`GetSessionsAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L82-L89) lists root
tokens, [`RevokeRefreshTokenFamilyAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L91-L105)
is "log out that device," and admin
[`ForceLogoutAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L100-L110) revokes
everything a user holds. (The session list has a display bug after rotation — find it in
doc 06, exercise 7.)

## Stage 5: Signatures, JWKS, and Key Rotation

Tokens are **RS256** — asymmetric. The provider signs with a private key; *anyone* can
verify with the public key. That's what makes JWTs stateless: resource servers verify
locally, no call to the provider per request.

- Signing: [`CreateJwtAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L94-L110)
  loads the newest unretired key, signs, and stamps the `kid` in the JWT header.
- Publication: [`GetJwksAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L68-L92)
  exports **public parameters only** (`ExportParameters(false)` — modulus and exponent) for
  the current key *plus keys retired within 7 days*.
- Rotation: [`KeyManagementService.RotateAsync`](../src/NetAuth0.Infrastructure/Security/KeyManagementService.cs#L10-L23)
  retires all current keys and creates a fresh RSA-2048 key. Triggered by
  `POST /api/v1/admin/signing-keys/rotate`.

Why serve retired keys for 7 days? Because tokens signed with the old key are still in the
wild for up to their lifetime. A verifier that fetched JWKS and found only the new `kid`
would reject *valid* tokens. The grace window must exceed the longest-lived token signed by
the old key — 15-minute access tokens make 7 days generous, which is the safe direction.

Why RS256 and not HS256? HS256 is symmetric — every verifier holds the *signing* key, so any
resource server could forge tokens. With RS256, the private key never leaves the provider
(here, "never leaves the database," which is its own problem — doc 08).

If signing keys vanished, [`GetCurrentSigningKeyAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L112-L130)
self-heals by generating one on first use — convenient bootstrap, and worth noticing because
it means *issuing a token can write to the database*.

## How the API Consumes Its Own Tokens

[`Program.cs:50-63`](../src/NetAuth0.Api/Program.cs#L50-L63): standard JwtBearer pointed at
the repo's own discovery document. Signature, issuer, and lifetime are validated;
**audience is not** (`ValidateAudience = false`) — so a token minted for any client works
against the admin API if it carries the right permission claims. Nor is the token's `typ`
claim: an ID token (`typ: "id"`, issued at
[`JwtTokenIssuer.cs:36-44`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L36-L44))
has a `sub` and a valid signature, so it is accepted as a bearer token on the
`[Authorize]`-only endpoints in `AuthController` (MFA enrollment, sessions). File both
thoughts for doc 08. Authorization is claim-based policies, one per permission string
([`Program.cs:65-71`](../src/NetAuth0.Api/Program.cs#L65-L71)), consumed via
`[Authorize(Policy = "applications:manage")]` in
[`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L20-L25).

## Checkpoint

Answer with file/line citations:

1. List every distinct secret in the authorization-code flow and its storage form.
2. Why must the `redirect_uri` be presented *again* at the token exchange when it was
   already validated at authorize time?
3. A mobile app's refresh token is exfiltrated by malware, used once by the attacker, and
   the victim's app refreshes an hour later. Walk both requests through
   `ExchangeRefreshTokenAsync` and state the end state of the family.
4. After `POST /signing-keys/rotate`, a token signed 1 minute earlier hits the admin API.
   Exactly why does validation still succeed?

<details><summary>Answers</summary>

1. Password (BCrypt hash, [`SecurityServices.cs:15`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L15)),
   client secret (BCrypt hash), authorization code (SHA-256 hash), PKCE verifier (never
   stored — only its SHA-256 challenge on the code row), refresh token (SHA-256 hash),
   signing key (plaintext PEM in DB — the outlier), TOTP secret (plaintext — needed for
   computation), recovery codes (SHA-256 hashes).
2. RFC 6749 §4.1.3 binding: it proves the exchanger saw the same redirect the code was
   issued for, blocking mix-up attacks where a code issued under one redirect is redeemed
   claiming another. Implemented at [`OAuthService.cs:128-131`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L128-L131).
3. Attacker's use: token is active → rotated, attacker now holds the only live child.
   Victim's use an hour later: presented token has `ReplacedById` set → `IsActive` false →
   `RevokeFamilyForReuseAsync` marks the whole family (including the attacker's child)
   reused+revoked; audit `refresh_token.reuse_detected`. Attacker's next refresh fails.
   Net: one attacker access-token window (≤15 min of API access per rotation), then ejection.
4. The old key is retired, not deleted; JWKS still serves it for 7 days
   ([`JwtTokenIssuer.cs:71`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L71)).
   The token's header `kid` selects the old public key. (JwtBearer also caches JWKS and
   refreshes on unknown-`kid`, so even fetch timing doesn't break it.)

</details>
