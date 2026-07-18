# Pattern Catalog

## Pattern: Thin Controller, Application Service Owns Business Flow

**Problem:** HTTP controllers should not contain OAuth, login, or persistence rules.

**General shape:** controller binds transport data, calls an application service, returns a response.

**Real example:** [`OAuthController.Token`](../../../src/NetAuth0.Api/Controllers/OAuthController.cs#L34-L59)

**Second:** [`OAuthService.ExchangeTokenAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L71-L80)

**Why it works:** the controller stays about form binding and response shape; the grant switch lives in application code.

**Failure modes:** exceptions become generic problem details unless mapped carefully; missing validators let malformed requests reach the service.

**Use when:** request handling crosses multiple domain/persistence operations.

**Avoid when:** the endpoint is pure health/static metadata.

**Drill:** move one validation branch from service to a FluentValidation validator in a scratch branch. What gets better? What gets harder?

## Pattern: Direct EF Query Through Context Abstraction

**Problem:** application services need persistence without referencing concrete infrastructure.

**General shape:** inject `IAppDbContext`, query `DbSet<T>`, call `SaveChangesAsync`.

**Real example:** [`IdentityService.LoginAsync`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95)

**Second:** [`IAppDbContext`](../../../src/NetAuth0.Application/Abstractions/IAppDbContext.cs)

**Why it works:** low ceremony, LINQ stays visible, and EF features like `Include`, projections, and transactions remain available.

**Failure modes:** services can grow query-heavy; tests often need EF/in-memory substitutes; query reuse can become inconsistent.

**Use when:** the project is small/medium and EF is the primary persistence model.

**Avoid when:** multiple data stores or complex query ownership require repositories/specifications.

**Drill:** identify three queries that would become shared if the admin dashboard added tenant-scoped reporting.

## Pattern: Domain Entity Methods For Invariants

**Problem:** security state transitions should not be arbitrary property edits.

**General shape:** private setters plus methods such as `Consume`, `RotateTo`, `MarkReuse`, `Block`.

**Real example:** [`RefreshToken`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L239-L278)

**Second:** [`User.RegisterFailure`](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L125-L136)

**Why it works:** callers can express intent, and invariants remain close to data.

**Failure modes:** anemic methods can still allow invalid cross-entity state; EF private constructors can hide invalid objects if mappings are loose.

**Use when:** state changes have security or lifecycle meaning.

**Avoid when:** the method only repeats a setter without enforcing an invariant.

**Drill:** add a domain method proposal for `SigningKey` expiration. What invariant should it protect?

## Pattern: Claim-Based Permission Policies

**Problem:** admin endpoints need per-action authorization.

**General shape:** token issuer emits `permissions` claims; ASP.NET policies require a specific claim.

**Real example:** [`Program` authorization policies](../../../src/NetAuth0.Api/Program.cs#L65-L71)

**Second:** [`JwtTokenIssuer.IssueUserTokensAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53)

**Why it works:** easy to wire to `[Authorize(Policy = "...")]`.

**Failure modes:** permission strings can drift; tenant/resource checks still need service-level enforcement.

**Use when:** permissions are stable and small enough to fit in tokens.

**Avoid when:** permissions change frequently or require real-time revocation.

**Drill:** find every place a permission string appears. Propose one central constant location.

## Pattern: React Query For Server State

**Problem:** admin views need loading, error, caching, invalidation, and refetch behavior.

**General shape:** `useQuery` for reads, `useMutation` for writes, invalidate relevant query keys after success.

**Real example:** [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L21-L78)

**Second:** [`DashboardPage`](../../../frontend/src/pages/DashboardPage.tsx#L5-L48)

**Why it works:** server state is not confused with local form state.

**Failure modes:** query keys can miss dimensions such as tenant ID; invalidation can be too broad or too narrow.

**Use when:** data is fetched from APIs and reused across screens.

**Avoid when:** state is purely local UI state.

**Drill:** explain why `['apps', tenantId]` is a better cache key than `['apps']`.

## Pattern: Vetted Crypto Primitives Behind Interfaces

**Problem:** auth systems need randomness, hashing, password verification, signing, and TOTP without inventing crypto.

**General shape:** infrastructure implements small abstractions with `RandomNumberGenerator`, BCrypt, SHA-256, RSA, and Otp.NET.

**Real example:** [`SecurityServices`](../../../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L13-L51)

**Second:** [`JwtTokenIssuer.CreateJwtAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L94-L130)

**Why it works:** callers ask for security operations, not algorithms.

**Failure modes:** wrapping crypto can hide unsafe policy choices such as accepting `plain` PKCE or weak key storage.

**Use when:** the wrapper expresses domain intent and delegates to vetted libraries.

**Avoid when:** the wrapper invents a custom algorithm.

**Drill:** mark which lines generate randomness, hash public verifiers, hash passwords, and sign JWTs.
