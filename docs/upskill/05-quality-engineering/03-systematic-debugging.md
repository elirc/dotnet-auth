# Systematic Debugging

Use this loop: reproduce -> narrow the layer -> form one hypothesis -> test cheaply -> fix root cause -> add regression coverage.

## Scenario: Login Succeeds But Dashboard Calls Return 401

**Reproduction:** submit login from [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L46-L53), then open dashboard.

**First question:** did the frontend store a token, or did the API reject it?

**Narrowing path:**

1. Inspect `localStorage.netauth0.access_token`, set in [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L33-L37).
2. Confirm `request` attaches bearer header in [`api/client.ts`](../../../frontend/src/api/client.ts#L66-L79).
3. Confirm auth middleware is registered in [`Program`](../../../src/NetAuth0.Api/Program.cs#L131-L136).
4. Decode token and check issuer against [`Program`](../../../src/NetAuth0.Api/Program.cs#L49-L63).

**Useful probes:** browser devtools network tab, API logs from Serilog request logging in [`Program`](../../../src/NetAuth0.Api/Program.cs#L112-L123).

**Likely root causes:** missing token, wrong issuer, signing key not available through JWKS, expired token.

**Regression test to add:** WebApplicationFactory login + authenticated admin request.

**Senior lesson:** auth bugs are usually boundary bugs: token storage, header transport, validation configuration, claims, or authorization policy.

## Scenario: Refresh Token Replay Revokes Unexpected Sessions

**Reproduction:** exchange authorization code, refresh once, then refresh again with the original token.

**First question:** is this expected replay detection or a bug?

**Narrowing path:**

1. Read [`ExchangeRefreshTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L175-L230).
2. Inspect `IsActive` and `MarkReuse` in [`RefreshToken`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L266-L277).
3. Check whether the replacement token was persisted before the original was marked replaced.

**Useful probes:** EF SQL logging, DB rows grouped by `FamilyId`, breakpoints around the two `SaveChangesAsync` calls.

**Likely root causes:** expected replay, partial update between replacement insert and old-token rotation, clock issue, duplicate request retry.

**Regression test to add:** first refresh succeeds, second use of old token revokes family, replacement token then fails.

**Senior lesson:** multi-row security state transitions need transactions or carefully designed idempotency.

## Scenario: Admin Can See Or Change Another Tenant's Resource

**Reproduction:** obtain an admin token for tenant A, call an admin endpoint with tenant B in query/body.

**First question:** does authorization check only permission, or permission plus tenant?

**Narrowing path:**

1. Inspect permission policies in [`Program`](../../../src/NetAuth0.Api/Program.cs#L65-L71).
2. Inspect tenant parameter flow in [`AdminController.Users`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L27-L32).
3. Inspect tenant filtering in [`AdminService.SearchUsersAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L73-L89).
4. Compare with token tenant claim emitted by [`JwtTokenIssuer`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L26-L34).

**Useful probes:** integration test with two tenants, decoded token claims.

**Likely root causes:** tenant ID trusted from request, no tenant membership table, no policy handler that compares resource tenant to token tenant.

**Regression test to add:** cross-tenant admin request returns 403.

**Senior lesson:** authentication answers "who are you"; authorization answers "can you do this to this resource?"

## Scenario: Application List Looks Stale After Creating Client

**Reproduction:** create an application from [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L54-L66), list does not update.

**First question:** did mutation succeed, and did the right query key invalidate?

**Narrowing path:**

1. Confirm mutation calls [`api.createApplication`](../../../frontend/src/api/client.ts#L88-L89).
2. Confirm invalidation uses `['apps', tenantId]` in [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L44-L47).
3. Confirm read query uses the same key in [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L23-L25).

**Useful probes:** React Query devtools, network tab.

**Likely root causes:** tenant ID empty/stale, mutation failed but error not noticed, invalidation key mismatch.

**Regression test to add:** component test that mutation success invalidates the applications query.

**Senior lesson:** query keys are cache contracts.

## Scenario: Backend Fails On First Run

**Reproduction:** run API with PostgreSQL.

**First question:** migration failure, schema creation failure, or seed failure?

**Narrowing path:**

1. Check connection string in `appsettings.json`.
2. Check startup schema branch in [`Program`](../../../src/NetAuth0.Api/Program.cs#L138-L150).
3. Check seed data in [`DbSeeder`](../../../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L9-L47).

**Useful probes:** startup logs, PostgreSQL logs, `dotnet ef database update` once SDK is installed.

**Likely root causes:** no DB, no migrations, EnsureCreated mismatch after later migrations, duplicate seed data.

**Regression test to add:** integration startup test using Testcontainers.

**Senior lesson:** schema management is deployment logic; fallback creation is helpful locally but risky in production.
