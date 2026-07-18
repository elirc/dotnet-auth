# Comparative Implementations

## Comparison 1: Controllers vs Minimal APIs

**The decision this repo made:** MVC controllers own HTTP endpoint grouping and model binding - [`OAuthController`](../../../src/NetAuth0.Api/Controllers/OAuthController.cs#L8-L93).

**The alternative:** minimal APIs route endpoint delegates from `Program`.

```csharp
// Illustrative: not from this repo
app.MapPost("/connect/token", async (TokenForm form, IOAuthService oauth, RequestContext ctx, CancellationToken ct) =>
    Results.Ok(await oauth.ExchangeTokenAsync(Map(form), ctx, ct)));
```

| Dimension | This repo's choice | Alternative |
| --- | --- | --- |
| Complexity / ceremony | More files and attributes, clearer grouping. | Less ceremony, easy to see route registration inline. |
| Testability | Controller tests are familiar; service remains primary test target. | Endpoint filters and delegates can be tested, but large delegates get messy. |
| Performance characteristics | MVC overhead is fine for this project. | Minimal APIs can be slightly leaner. |
| Coupling & blast radius | Controllers isolate HTTP surface from startup file. | `Program` can become crowded unless endpoints are modularized. |
| Failure modes unique to it | Attribute drift, duplicated route conventions. | Business logic leaking into route lambdas. |
| Migration cost in this repo | Moderate: four controllers and Swagger/auth attributes need endpoint equivalents. | Low for small metadata endpoints, higher for token form binding. |

**What each choice commits you to / who pays:** controllers commit maintainers to a conventional ASP.NET structure; minimal APIs commit maintainers to stronger route organization discipline.

**Verdict for this repo:** right call. OAuth and admin endpoints benefit from conventional grouping.

**Switch signal:** revisit if endpoint count stays small and controller ceremony starts obscuring flow.

**Drill:** migrate `OidcController.Discovery` to a minimal endpoint in a scratch branch.

**Self-grade:** Basic = route compiles. Solid = OpenAPI still shows it. Strong = auth/rate-limit behavior is preserved.

## Comparison 2: Direct EF Context vs Repository Pattern

**The decision this repo made:** application services query `IAppDbContext` directly - [`AdminService.SearchUsersAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L73-L89).

**The alternative:** repositories hide EF behind named methods.

```csharp
// Illustrative: not from this repo
public interface IUserRepository
{
    Task<PagedResult<UserDto>> SearchAsync(Guid tenantId, string? query, Page page, CancellationToken ct);
}
```

| Dimension | This repo's choice | Alternative |
| --- | --- | --- |
| Complexity / ceremony | Low ceremony; queries are visible at call site. | More types and indirection. |
| Testability | Integration-style EF tests are natural; pure unit tests are harder. | Repositories can be mocked, but mocks can lie about LINQ/SQL behavior. |
| Performance characteristics | Query shape is visible, including projection and pagination. | Query performance may be hidden behind method names. |
| Coupling & blast radius | Application layer knows EF concepts like `DbSet`, `Include`, `IQueryable`. | Application layer depends on repository contracts; EF-specific changes are localized. |
| Failure modes unique to it | Query duplication and accidental unbounded queries. | Anemic repositories that become one method per LINQ query. |
| Migration cost in this repo | Moderate: `IdentityService`, `OAuthService`, `AdminService`, `JwtTokenIssuer`, and `AuditWriter` all use context. | N/A |

**What each choice commits you to / who pays:** direct context commits service authors to understanding EF. Repository commits maintainers to designing query ownership and not just wrapping CRUD.

**Verdict for this repo:** defensible. The service count is small and EF is the only persistence model.

**Switch signal:** revisit when the same tenant-scoped queries repeat across several services or when non-EF storage appears.

**Migration Drill:** design the move from direct `IAppDbContext` to repositories.

1. List services that use `IAppDbContext`.
2. Group methods by aggregate/query ownership: users, applications, tokens, audit.
3. Create one repository for a repeated query only, not every entity.
4. Write integration tests for old and new query shape.
5. Migrate one read path first, behind existing service contract.
6. Roll back by restoring service query and deleting the new repository registration.

**Self-grade:** Basic = identifies touched services. Solid = includes tests and rollback. Strong = avoids generic CRUD repositories.

## Comparison 3: JWT Bearer Admin Auth vs Cookie Session

**The decision this repo made:** admin endpoints require JWT bearer claims and permission policies - [`Program`](../../../src/NetAuth0.Api/Program.cs#L49-L71), [`AdminController`](../../../src/NetAuth0.Api/Controllers/AdminController.cs#L8-L78).

**The alternative:** browser admin dashboard uses secure SameSite cookies and anti-forgery protection.

```csharp
// Illustrative: not from this repo
builder.Services.AddAuthentication().AddCookie("AdminCookie");
builder.Services.AddAntiforgery();
```

| Dimension | This repo's choice | Alternative |
| --- | --- | --- |
| Complexity / ceremony | Simple for API clients and SPAs. | More server-side session and CSRF details. |
| Testability | Token claims are easy to construct in tests. | Cookie auth requires test server cookie handling. |
| Performance characteristics | Stateless validation, but token revocation is hard. | Server/session lookup may add state, revocation is easier. |
| Coupling & blast radius | Frontend stores token in `localStorage` via [`LoginPage`](../../../frontend/src/pages/LoginPage.tsx#L33-L37). | Frontend becomes less aware of tokens, API must handle cookies. |
| Failure modes unique to it | XSS can steal tokens; permission updates wait for token expiry. | CSRF risk if anti-forgery/SameSite are wrong. |
| Migration cost in this repo | High: login response, fetch wrapper, auth middleware, CORS, and tests change. | N/A |

**What each choice commits you to / who pays:** JWT commits frontend and API teams to token lifecycle hygiene; cookies commit backend to CSRF/session correctness.

**Verdict for this repo:** defensible for OAuth learning, questionable for the first-party admin dashboard.

**Switch signal:** revisit if the dashboard becomes production-facing or needs immediate admin revocation.

**Drill:** write a threat model comparing XSS token theft vs CSRF cookie abuse for this dashboard.

**Self-grade:** Basic = names both risks. Solid = names mitigations. Strong = ties mitigations to exact files.

## Comparison 4: React Query vs Hand-Rolled Fetch State

**The decision this repo made:** server state uses TanStack Query - [`DashboardPage`](../../../frontend/src/pages/DashboardPage.tsx#L5-L48), [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L21-L78).

**The alternative:** each component uses `useEffect`, `useState`, and manual loading/error flags.

```tsx
// Illustrative: not from this repo
useEffect(() => {
  setLoading(true);
  fetchApps(tenantId).then(setApps).catch(setError).finally(() => setLoading(false));
}, [tenantId]);
```

| Dimension | This repo's choice | Alternative |
| --- | --- | --- |
| Complexity / ceremony | Adds library concepts: query keys, invalidation, stale time. | Easier for one component, repetitive across many. |
| Testability | Can test cache invalidation behavior explicitly. | Tests inspect local state transitions per component. |
| Performance characteristics | Avoids duplicate fetches and supports caching. | Easy to over-fetch or race stale requests. |
| Coupling & blast radius | Query keys become API-level contracts. | Each component owns its own data lifecycle. |
| Failure modes unique to it | Bad query keys leak cross-tenant data in cache. | Stale closures, missing cleanup, inconsistent errors. |
| Migration cost in this repo | Low/moderate: only a few pages now, but pattern is already established. | N/A |

**What each choice commits you to / who pays:** React Query commits frontend maintainers to disciplined query keys; manual fetch commits every component author to rebuilding server-state machinery.

**Verdict for this repo:** right call.

**Switch signal:** do not switch globally; only avoid Query for local UI state.

**Drill:** explain why [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L44-L47) invalidates `['apps', tenantId]` after creation.

**Self-grade:** Basic = says "refetch list". Solid = explains cache key scope. Strong = identifies tenant ID as isolation dimension.
