# Good First Tickets

Because this repo is synthetic, treat these as simulated maintainer tickets. The reviewer expects small blast radius, tests when feasible, and explicit risk notes.

## Ticket 1: Centralize Permission Constants

**Difficulty:** Easy  
**Time:** 1-2 hours  
**Skills:** C#, authorization, refactoring

**Story:** As a maintainer, I want permission names defined once so policy strings and token claims do not drift.

**Why it's a good contribution:** Small blast radius; touches policy registration and seed data.

**Acceptance criteria:**

- [ ] Create a constants class for `users:manage`, `applications:manage`, `roles:manage`, `audit:read`.
- [ ] Replace strings in [`Program`](../../../src/NetAuth0.Api/Program.cs#L65-L71) and [`DbSeeder`](../../../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L31-L37).
- [ ] Build backend when .NET SDK is available.

**Read first:** [`Program`](../../../src/NetAuth0.Api/Program.cs#L65-L71) - policy source.

**Files likely touched:** API or Application constants file, `Program.cs`, `DbSeeder.cs`.

**What could go wrong:** placing constants in Infrastructure would invert dependency direction.

**Review questions:** Does the dependency point inward? Did every string get replaced?

## Ticket 2: Validate Application Redirect URI Syntax

**Difficulty:** Easy  
**Time:** 2-3 hours  
**Skills:** validation, security

**Story:** As an admin, I want invalid callback URLs rejected so unsafe clients cannot be registered.

**Acceptance criteria:**

- [ ] Reject empty, relative, whitespace-padded, and fragment-containing redirect URIs.
- [ ] Add tests around [`AdminService.CreateApplicationAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L28-L54).
- [ ] Keep exact-match runtime check in [`ValidateRedirectUri`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L248-L254).

**Files likely touched:** `AdminService`, validation class if introduced, tests.

**What could go wrong:** over-rejecting localhost dev callbacks.

**Review questions:** Does validation differ between dev and production? Is the error useful?

## Ticket 3: Add Audit Event For Signing Key Rotation

**Difficulty:** Easy  
**Time:** 1-2 hours  
**Skills:** audit logging, admin operations

**Story:** As a security operator, I want key rotations recorded in the audit log.

**Acceptance criteria:**

- [ ] Rotation endpoint emits `signing_key.rotated`.
- [ ] Event includes new key ID, actor context, IP, and user agent where available.
- [ ] Existing JWKS behavior remains unchanged.

**Read first:** [`AdminController.RotateSigningKey`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L72-L77), [`JwtTokenIssuer.GetJwksAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L68-L92).

**What could go wrong:** writing audit in the wrong layer or losing request context.

## Ticket 4: Improve Token Endpoint OAuth Error Shape

**Difficulty:** Medium  
**Time:** 3-5 hours  
**Skills:** OAuth, error handling, ASP.NET

**Story:** As an OAuth client, I want token errors returned with OAuth-compatible `error` and `error_description` fields.

**Acceptance criteria:**

- [ ] Token endpoint distinguishes invalid client, invalid grant, unsupported grant type.
- [ ] Existing problem details remain for non-OAuth API errors.
- [ ] Add endpoint tests when .NET SDK is available.

**Read first:** [`OAuthController.Token`](../../../src/NetAuth0.Api/Controllers/OAuthController.cs#L34-L59), [`Program` exception handler](../../../src/NetAuth0.Api/Program.cs#L112-L123).

**What could go wrong:** leaking sensitive credential details.

## Ticket 5: Add Frontend Empty State For Missing Tenant ID

**Difficulty:** Easy  
**Time:** 1 hour  
**Skills:** React, UX

**Story:** As a learner, I want dashboard pages to explain missing tenant context clearly.

**Acceptance criteria:**

- [ ] Applications, Users, and Audit pages show a clear state when `session.tenantId` is empty.
- [ ] Existing [`AsyncState`](../../../frontend/src/components/AsyncState.tsx) style is reused.
- [ ] `npm run build` passes.

**Read first:** [`DashboardPage`](../../../frontend/src/pages/DashboardPage.tsx#L18-L26).

**What could go wrong:** fetching with empty tenant ID.

## Ticket 6: Add Integration Test Skeleton For Refresh Rotation

**Difficulty:** Medium  
**Time:** 4-6 hours  
**Skills:** xUnit, Testcontainers, OAuth

**Story:** As a maintainer, I want refresh-token replay behavior protected by integration tests.

**Acceptance criteria:**

- [ ] Testcontainers PostgreSQL starts API.
- [ ] Authorization-code flow creates refresh token.
- [ ] Reusing old refresh token revokes the family.

**Read first:** [`ExchangeRefreshTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L175-L230).

**What could go wrong:** test becomes too coupled to seed data; use explicit setup.
