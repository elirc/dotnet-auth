# Key Flows

## Flow: Hosted Login To Dashboard Token

**Why it matters:** Login is the learner's first auth boundary. It crosses React form state, public API rate limiting, password verification, lockout, MFA challenge, audit logging, and token issuance.

**Open first:** [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L19-L59) - shows how credentials and TOTP code enter the system.

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | UI | [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L22-L39) | `react-hook-form` submits tenant slug, email, password, optional TOTP. | `FormValues` | Default admin credentials are visible in dev defaults. |
| 2 | UI API client | [`api.login`](../../../frontend/src/api/client.ts#L81-L84) | Sends JSON to `/api/v1/auth/login`. | `LoginResponse` | Errors collapse into a single thrown `Error`. |
| 3 | API | [`AuthController.Login`](../../../src/NetAuth0.Api/Controllers/AuthController.cs#L22-L27) | Public endpoint calls application service. | `LoginRequest` | No FluentValidation validator exists yet. |
| 4 | Application | [`IdentityService.LoginAsync`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95) | Validates tenant, password, lockout, MFA, audit, then issues dashboard token. | `LoginResponse` | First-party login bypasses formal authorization-code flow for dashboard token. |
| 5 | Infrastructure | [`JwtTokenIssuer.IssueUserTokensAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53) | Embeds tenant, client, scope, and permissions into JWT claims. | Access token and ID token | Permissions are claim strings; policy drift is easy. |

**Validation & authorization:** public rate limit is configured in [`Program`](../../../src/NetAuth0.Api/Program.cs#L73-L83); controller is public in [`AuthController`](../../../src/NetAuth0.Api/Controllers/AuthController.cs#L10-L27).

**Persistence & side effects:** failed attempts mutate `User` in [`IdentityService`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L58-L70); login audit is written in [`IdentityService`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L86-L88).

**Tests:** No direct login test found. Existing test only covers SHA-256/constant-time helper behavior in [`SecurityTests`](../../../tests/NetAuth0.UnitTests/SecurityTests.cs#L6-L19).

**Juniors miss:** a successful password check is not enough; lockout, MFA state, audit, and token claims are part of correctness.

**Seniors notice:** issuing dashboard tokens directly from login is convenient, but it couples hosted login and first-party admin auth. A formal PKCE loop would be slower to build but cleaner.

**Drill:** write a trace table showing what happens when a user has verified MFA but submits no `totpCode`.

**Self-grade:** Basic = can point to the files. Solid = can explain every branch. Strong = can propose a regression test and a safer dashboard-token design.

## Flow: Authorization Code + PKCE

**Why it matters:** This is the core OAuth learning path. It tests exact redirect URI matching, code lifetime, code single use, and PKCE verifier comparison.

**Open first:** [`OAuthService.CreateAuthorizationCodeAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L25-L69).

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | API | [`OAuthController.Authorize`](../../../src/NetAuth0.Api/Controllers/OAuthController.cs#L11-L32) | Binds query parameters and forwards to service. | `AuthorizeRequest` | Current endpoint accepts `user_id` directly; real hosted login should bind to authenticated session. |
| 2 | Application | [`GetClientAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L232-L246) | Loads app with redirect URIs and grant types. | `ClientApplication` aggregate | Includes are explicit; missing include would break redirect/grant checks. |
| 3 | Application | [`ValidateRedirectUri`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L248-L254) | Exact ordinal redirect URI match. | Registered URI string | Allows only pre-registered URLs, preventing open redirect classes. |
| 4 | Application | [`ValidatePkce`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L256-L267) | Requires challenge for clients requiring PKCE. | challenge + method | `plain` is accepted; production policy might require `S256` only. |
| 5 | Domain | [`AuthorizationCode`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L204-L237) | Stores hashed code, redirect URI, nonce, challenge, expiry, consumed timestamp. | persisted code record | Code is short lived and single-use only if exchange consumes it. |
| 6 | Application | [`ExchangeAuthorizationCodeAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L107-L153) | Verifies hash, expiry, redirect URI, PKCE, consumes code, issues tokens and refresh token. | `TokenResponse` | Replay and concurrency need integration coverage. |

**Validation & authorization:** grant type is checked in [`GetClientAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L232-L246).

**Persistence & side effects:** code entity mapping has unique code hash and PKCE column sizes in [`AppDbContext`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L92-L99).

**Tests:** No direct authorization-code integration test found.

**Juniors miss:** `state` is reflected back but not validated server-side; clients own state validation.

**Seniors notice:** the current authorize endpoint is a learning shortcut because it accepts `user_id`. A production hosted login would bind the user through an authenticated browser session and consent decision.

**Drill:** identify every condition that must hold before a code can be exchanged.

**Self-grade:** Basic = can list checks. Solid = can map checks to line anchors. Strong = can design a replay/concurrency test.

## Flow: Refresh Token Rotation And Replay Detection

**Why it matters:** Refresh tokens are long-lived bearer secrets. Rotation narrows the replay window; family revocation makes theft visible.

**Open first:** [`RefreshToken`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L239-L278).

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | Domain | [`RefreshToken`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L254-L278) | Tracks token hash, family ID, replacement, revocation, reuse. | Token family | Family semantics are not obvious from one row. |
| 2 | Application | [`ExchangeRefreshTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L175-L230) | Hashes presented token, loads active row, checks expiry/reuse, creates replacement. | Old token + replacement | Two `SaveChangesAsync` calls create a partial-update risk. |
| 3 | Application | [`RevokeFamilyForReuseAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L300-L310) | Marks every family member as reused/revoked. | Token family | Needs test coverage for replay of already rotated token. |
| 4 | API | [`AuthController.RevokeSession`](../../../src/NetAuth0.Api/Controllers/AuthController.cs#L51-L57) | User-visible session revoke by family ID. | `familyId` route param | Controller relies on service filtering by current user. |

**Validation & authorization:** authenticated session endpoints are under `[Authorize]` in [`AuthController`](../../../src/NetAuth0.Api/Controllers/AuthController.cs#L29-L57).

**Persistence & side effects:** refresh token hash and family indexes are configured in [`AppDbContext`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L101-L106).

**Tests:** No direct refresh-token rotation test found.

**Juniors miss:** the raw refresh token is never stored; only a hash is stored.

**Seniors notice:** rotation should be transactional. If replacement insert succeeds and old-token revoke fails, replay semantics get muddy.

**Drill:** propose a test that proves replaying the first token after one successful refresh revokes the whole family.

**Self-grade:** Basic = explains family ID. Solid = identifies transactional risk. Strong = writes the test plan and rollback-safe migration for token family indexes.

## Flow: Admin Application Registration

**Why it matters:** Client registration is the control plane for OAuth. Errors here become security bugs in every downstream app.

**Open first:** [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L21-L78).

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | UI | [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L38-L47) | Splits comma-separated redirect URIs and grant types, then calls mutation. | Form values | String splitting is easy to make permissive or ambiguous. |
| 2 | UI API client | [`api.createApplication`](../../../frontend/src/api/client.ts#L88-L89) | Sends JSON to admin endpoint with bearer token. | app registration body | Body is `unknown`, so TypeScript does not enforce contract. |
| 3 | API | [`AdminController.CreateApplication`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L20-L25) | Requires `applications:manage` policy. | `CreateApplicationRequest` | Tenant ID comes from request, not token claim. |
| 4 | Application | [`AdminService.CreateApplicationAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L28-L54) | Generates client ID, optional secret, redirect URIs, grant types, audit event. | `ClientApplication` aggregate | No URL syntax validation beyond distinct strings. |
| 5 | Persistence | [`AppDbContext`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L36-L57) | Enforces unique client ID and unique redirect/grant rows. | relational schema | No compiled migration yet. |

**Validation & authorization:** policy registration is in [`Program`](../../../src/NetAuth0.Api/Program.cs#L65-L71), policy use in [`AdminController`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L13-L25).

**Persistence & side effects:** audit write occurs in [`AdminService`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L50-L53).

**Tests:** No direct admin-registration test found.

**Juniors miss:** admin authorization is not tenant authorization. A token with `applications:manage` still needs tenant/resource isolation.

**Seniors notice:** tenant ID should likely be derived from claims or checked against a tenant assignment table, not trusted from query/body alone.

**Drill:** write acceptance criteria for rejecting a redirect URI with whitespace, fragments, or a non-HTTPS production URL.

**Self-grade:** Basic = traces UI to DB. Solid = identifies contract gaps. Strong = designs tenant isolation and validation tests.

## Flow: JWKS And Key Rotation

**Why it matters:** JWT consumers must validate tokens during signing-key rotation. Downtime-free rotation means the JWKS serves current and recent retired keys.

**Open first:** [`JwtTokenIssuer.GetJwksAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L68-L92).

| Step | Owner | File | What happens | Data shape | Risk |
| --- | --- | --- | --- | --- | --- |
| 1 | API | [`OidcController.Discovery`](../../../src/NetAuth0.Api/Controllers/OidcController.cs#L9-L27) | Publishes OIDC metadata, including JWKS URI. | discovery document | Static list may drift from actual support. |
| 2 | API | [`OidcController.Jwks`](../../../src/NetAuth0.Api/Controllers/OidcController.cs#L29-L33) | Returns current plus recent retired keys. | JWKS | Consumers rely on stable `kid`. |
| 3 | Infrastructure | [`JwtTokenIssuer.GetJwksAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L68-L92) | Exports RSA public parameters only. | public key set | Private keys are stored as PEM in DB. |
| 4 | Admin | [`AdminController.RotateSigningKey`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L72-L77) | Rotates key through management endpoint. | `{ keyId }` | No audit event in key rotation service. |

**Validation & authorization:** key rotation requires `applications:manage` in [`AdminController`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L72-L77).

**Persistence & side effects:** signing key is mapped with unique key ID and text PEM in [`AppDbContext`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L108-L112).

**Tests:** No direct JWKS or rotation test found.

**Juniors miss:** JWKS must never expose private key material.

**Seniors notice:** private key storage should be encrypted or moved to KMS/HSM for serious environments.

**Drill:** sketch a no-downtime rotation runbook: rotate, publish, wait for token max lifetime, retire, monitor validation failures.

**Self-grade:** Basic = explains `kid`. Solid = explains current/previous keys. Strong = proposes operational metrics and rollback.
