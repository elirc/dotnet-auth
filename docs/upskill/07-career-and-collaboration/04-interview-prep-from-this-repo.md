# Interview Prep From This Repo

Use this as a mid-level interview track. Every answer should cite code and discuss tradeoffs, not just definitions.

## C# / .NET Runtime

**Question:** Where does async matter in the login path?

- Junior: "The methods return `Task` and use `await`."
- Mid-level: "`IdentityService.LoginAsync` awaits EF queries and saves with cancellation tokens in [`LoginAsync`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95)."
- Senior: "Async keeps request threads free during I/O, but correctness also depends on transaction boundaries and cancellation propagation. I would ask whether failed login mutation and audit write should share a transaction."

**Question:** What allocations or crypto work happen during token issuance?

- Junior: "It creates claims and signs a JWT."
- Mid-level: "`JwtTokenIssuer` builds claim lists and imports RSA PEM for signing in [`CreateJwtAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L94-L130)."
- Senior: "Importing RSA key per token is simple but may be expensive; I would measure issuance latency before caching key material and would avoid unsafe singleton private-key lifetime unless protected."

## ASP.NET Core / EF Core / React

**Question:** Explain the ASP.NET pipeline in this app.

- Junior: "It uses controllers and Swagger."
- Mid-level: "`Program` registers CORS, rate limiting, authentication, authorization, health checks, and controllers in order in [`Program`](../../../src/NetAuth0.Api/Program.cs#L125-L136)."
- Senior: "Order matters: rate limiting before auth is acceptable for public auth endpoints, but production error handling should distinguish OAuth errors from generic 400 problem details."

**Question:** How does EF enforce uniqueness and concurrency?

- Junior: "There are indexes."
- Mid-level: "`AppDbContext` configures unique indexes for tenant slug, client ID, redirect URIs, grant types, and token hashes in [`OnModelCreating`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L28-L129)."
- Senior: "The mapping uses `xmin` concurrency for tenant/application/user, but token rotation still needs transactional tests because multiple rows participate in one logical state transition."

**Question:** Why use React Query here?

- Junior: "It fetches data."
- Mid-level: "`DashboardPage` and `ApplicationsPage` use query keys with tenant ID in [`DashboardPage`](../../../frontend/src/pages/DashboardPage.tsx#L5-L9) and invalidation in [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L44-L47)."
- Senior: "Query keys are part of data isolation. If tenant ID is missing, cache can show data in the wrong tenant context."

## Debugging

**Question:** A refresh token works once, then all sessions are revoked. Where do you look?

- Junior: "Look at refresh-token code."
- Mid-level: "Start in [`ExchangeRefreshTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L175-L230), then [`RefreshToken.IsActive`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L266-L277)."
- Senior: "Determine whether this is expected replay detection or a persistence ordering bug. Add a test for first use, second use of old token, and use of replacement."

**Question:** Application creation returns 403 despite login success. What layers can fail?

- Junior: "Maybe token missing."
- Mid-level: "Check `localStorage` token set in [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L33-L37), bearer header in [`request`](../../../frontend/src/api/client.ts#L66-L79), and `applications:manage` policy in [`AdminController`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L20-L25)."
- Senior: "Also inspect token claims from [`JwtTokenIssuer`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53) and seeded role assignment in [`DbSeeder`](../../../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L30-L44)."

## System Design

**Question from comparison card:** Defend controllers over minimal APIs here.

- Junior: "Controllers are organized."
- Mid-level: "OAuth and admin endpoints have multiple actions and attributes, so controller grouping is clear."
- Senior: "Controllers are right for conventional OpenAPI/auth grouping. I would use minimal APIs only for simple metadata endpoints if they reduced ceremony without mixing business logic into startup."

**Question from comparison card:** Defend direct EF context over repositories.

- Junior: "It is simpler."
- Mid-level: "The service queries are visible, such as [`AdminService.SearchUsersAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L73-L89)."
- Senior: "It is defensible while EF is the only store and query count is small. I would introduce repositories only around repeated query ownership or non-EF persistence, not as generic CRUD wrappers."

**Question from comparison card:** JWT bearer vs cookies for the admin dashboard?

- Junior: "JWT works with SPAs."
- Mid-level: "The dashboard stores an access token in local storage in [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L33-L37)."
- Senior: "Good for OAuth learning, but a production first-party admin may prefer secure cookies plus CSRF defense to reduce XSS token theft risk and improve revocation."

## Code Review

**Prompt:** Review a PR that adds a new admin endpoint using only `[Authorize]`.

- Junior answer: "It has auth."
- Mid-level adds: "It needs a specific policy like [`AdminController`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L13-L77)."
- Senior includes: "A policy is not enough. Check tenant/resource isolation, audit event, validation, error behavior, tests, and whether the token claim can authorize this tenant."

## Behavioral

**Prompt:** Tell me about a time you reduced risk in an auth change.

Use this repo as the story scaffold:

- Situation: refresh-token rotation has multiple writes in [`ExchangeRefreshTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L209-L228).
- Task: make replay detection reliable.
- Action: add integration tests, wrap rotation in a transaction, record audit events, and monitor replay failures.
- Result: safer token lifecycle with clear rollback.
