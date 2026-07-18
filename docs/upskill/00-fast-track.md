# Fast Track

Weekend objective: build the frontend, understand the API shape, and trace two end-to-end flows without pretending the backend was locally verified.

## Commands

| Command | Verified? | Notes |
| --- | --- | --- |
| `docker compose up -d` | Inferred | Compose exists, but was not run during the docs pass. |
| `dotnet run --project src/NetAuth0.Api` | Not verified | `dotnet --version` failed because the SDK is not installed. |
| `cd frontend; npm install` | Verified | Completed after a longer retry. |
| `cd frontend; npm audit --audit-level=moderate` | Verified | `found 0 vulnerabilities`. |
| `cd frontend; npm run build` | Verified | TypeScript and Vite production build passed. |

## First 10 Files

Read these in order:

1. [`Program`](../../src/NetAuth0.Api/Program.cs#L21-L154) - service registration, middleware, auth, rate limiting, startup schema behavior.
2. [`IdentityEntities`](../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L19-L355) - the nouns and invariants.
3. [`AppDbContext`](../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L7-L131) - tables, relationships, indexes, optimistic concurrency.
4. [`DependencyInjection`](../../src/NetAuth0.Infrastructure/DependencyInjection.cs#L16-L35) - DI lifetimes and boundary wiring.
5. [`OAuthController`](../../src/NetAuth0.Api/Controllers/OAuthController.cs#L8-L93) - public OAuth endpoints and form binding.
6. [`OAuthService`](../../src/NetAuth0.Application/OAuth/OAuthService.cs#L25-L311) - grant handling, PKCE, refresh rotation.
7. [`JwtTokenIssuer`](../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L130) - claims, RS256 signing, JWKS.
8. [`IdentityService`](../../src/NetAuth0.Application/Identity/IdentityService.cs#L31-L149) - signup, login, lockout, MFA, sessions.
9. [`api`](../../frontend/src/api/client.ts#L50-L100) - frontend token storage and fetch wrapper.
10. [`ApplicationsPage`](../../frontend/src/pages/ApplicationsPage.tsx#L21-L78) - React Query mutation and cache invalidation.

## Trace Two Flows

Flow A: hosted login. Start in [`LoginPage`](../../frontend/src/pages/LoginPage.tsx#L19-L59), move through [`api.login`](../../frontend/src/api/client.ts#L81-L84), then [`AuthController.Login`](../../src/NetAuth0.Api/Controllers/AuthController.cs#L22-L27), then [`IdentityService.LoginAsync`](../../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95), then [`JwtTokenIssuer.IssueUserTokensAsync`](../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53).

Flow B: application registration. Start in [`ApplicationsPage`](../../frontend/src/pages/ApplicationsPage.tsx#L38-L47), move through [`api.createApplication`](../../frontend/src/api/client.ts#L88-L89), then [`AdminController.CreateApplication`](../../src/NetAuth0.Api/Controllers/AdminController.cs#L20-L25), then [`AdminService.CreateApplicationAsync`](../../src/NetAuth0.Application/Admin/AdminService.cs#L28-L54), then the EF mapping in [`AppDbContext`](../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L36-L57).

## One Safe Change

Add a frontend-only empty-state improvement to the Users page. It should touch UI only and follow the existing [`AsyncState`](../../frontend/src/components/AsyncState.tsx) pattern. Run `npm run build`. Do not touch token or authorization code for your first change.

## Teach-Back

In five minutes, explain:

1. What is the difference between a tenant, client application, user, role, and permission?
2. Where is redirect URI exact matching enforced?
3. Why does refresh-token rotation need a family ID?
4. Which commands are verified and which are inferred?

## What This Skips

This path skips real backend execution, migrations, integration tests, browser visual verification, and production hardening. Those are not optional in real delivery; they are just outside this weekend win.
