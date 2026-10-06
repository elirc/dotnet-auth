# 02 — Architecture

Goal: understand not just *what* the structure is, but *why it was chosen*, what it costs,
and when you'd choose differently. Mid-level engineers know the rules; seniors know the
prices.

## The Layering, Precisely

Four projects, one direction of dependency:

| Project | Depends on | Owns | Must never contain |
| --- | --- | --- | --- |
| `NetAuth0.Domain` | nothing | entities, enums, invariant methods | I/O, EF attributes, HTTP types |
| `NetAuth0.Application` | Domain | services, contracts (request/response records), abstractions | concrete EF, crypto libraries, HTTP types |
| `NetAuth0.Infrastructure` | Application, Domain | `AppDbContext`, JWT issuing, BCrypt/TOTP/RNG, seeding | controllers, business rules |
| `NetAuth0.Api` | Application, Infrastructure (composition only) | controllers, startup, middleware, policies | queries, business rules |

The composition root is [`Program.cs`](../src/NetAuth0.Api/Program.cs) calling
[`AddInfrastructure`](../src/NetAuth0.Infrastructure/DependencyInjection.cs#L16-L35). The API
project is the **only** place allowed to know that `ITokenIssuer` is `JwtTokenIssuer`.

**The one deliberate impurity:** Application's
[`IAppDbContext`](../src/NetAuth0.Application/Abstractions/IAppDbContext.cs) exposes
`DbSet<T>` and services write LINQ directly (e.g.
[`AdminService.SearchUsersAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L73-L89)).
That couples Application to EF's abstractions (`DbSet`, `Include`, `IQueryable` semantics).
The alternative — repositories — would decouple it at the cost of a layer of indirection and
the classic failure mode of one-repository-method-per-query. For a codebase with ~5 services
and one store, direct context is the right trade. The *switch signal* is repetition: when the
same tenant-scoped query appears in three services, extract a repository for that query —
not a generic `IRepository<T>`.

## DI Lifetimes — and Why Each One

From [`DependencyInjection.cs`](../src/NetAuth0.Infrastructure/DependencyInjection.cs#L18-L32):

| Service | Lifetime | Why |
| --- | --- | --- |
| `AppDbContext` / `IAppDbContext` | Scoped | a DbContext is a unit of work; one per request, never shared across threads |
| `IClock`, `IPasswordHasher`, `ISecretGenerator`, `ITotpService` | Singleton | stateless, thread-safe, no dependencies |
| `ITokenIssuer`, `IKeyManagementService`, `IAuditWriter`, all `I*Service` | Scoped | they take the scoped DbContext, so they must be scoped or shorter |
| `RequestContext` | Scoped, built from `IHttpContextAccessor` ([`Program.cs:87-94`](../src/NetAuth0.Api/Program.cs#L87-L94)) | snapshots IP/user-agent/principal so Application code never touches `HttpContext` |

The rule worth internalizing: **a service's lifetime can never be longer than its
dependencies'.** A singleton holding a scoped DbContext is a captive dependency — it would
hold one DbContext forever, across requests and threads. ASP.NET's scope validation catches
this in Development, but you should catch it at review time.

`RequestContext` is an underrated trick: it converts an ambient, framework-bound object
(`HttpContext`) into a plain record ([`SecurityAbstractions.cs:39`](../src/NetAuth0.Application/Abstractions/SecurityAbstractions.cs#L39))
that Application services can take as a constructor dependency and tests can construct in
one line.

## Anatomy of a Request

Middleware order in [`Program.cs:112-136`](../src/NetAuth0.Api/Program.cs#L112-L136) — order
is behavior, not style:

```
Serilog request logging
→ exception handler        (converts unhandled exceptions to ProblemDetails)
→ Swagger (dev only)
→ CORS ("dev" policy: only http://localhost:5173)
→ rate limiter             (per-IP fixed window, only on endpoints opted in via
                            [EnableRateLimiting("public-auth")])
→ authentication           (JWT bearer: validates signature against own JWKS)
→ authorization            (claim policies: "users:manage" etc.)
→ /healthz, controllers
```

Three things to notice, in ascending order of subtlety:

1. **Rate limiting runs before authentication.** Correct for login/token endpoints: you want
   to shed brute-force traffic before doing BCrypt work. The partition key is the raw
   `RemoteIpAddress` ([`Program.cs:76`](../src/NetAuth0.Api/Program.cs#L76)) with no
   forwarded-headers handling, so behind a proxy every user shares one bucket of 20/min —
   an outage waiting to happen in real deployment.
2. **The API validates its own tokens by HTTP.** JwtBearer's `Authority` points at the
   service itself ([`Program.cs:53-54`](../src/NetAuth0.Api/Program.cs#L53-L54)); on first
   authenticated request it fetches its own discovery document and JWKS. Elegant (key
   rotation "just works") and circular (the API must be able to reach itself by its issuer
   URL).
3. **The exception handler returns 400 for *everything***
   ([`Program.cs:113-123`](../src/NetAuth0.Api/Program.cs#L113-L123)). Business failures in
   this codebase are thrown as `InvalidOperationException` and become 400s — but so would a
   `NullReferenceException` or a database outage. Genuine server faults are reported as
   client errors, which corrupts your error-rate metrics and misleads API consumers.
   Doc 10 has a ticket for this.

## The Data Model

Fifteen entity classes; the schema is `identity` in PostgreSQL
([`AppDbContext.cs:27`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L27)).
The mapping decisions worth studying:

- **Uniqueness as a backstop, not a primary check.** Services check duplicates with
  `AnyAsync` first (e.g. signup,
  [`IdentityService.cs:37`](../src/NetAuth0.Application/Identity/IdentityService.cs#L37)),
  but the unique index on `(TenantId, Email)`
  ([`AppDbContext.cs:61`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L61))
  is what actually guarantees it under concurrency. Two simultaneous signups both pass the
  `AnyAsync`; one then fails on the index. The check-then-act is for friendly errors; the
  constraint is for correctness. This pattern — *optimistic check, constraint backstop* —
  is everywhere in production systems.
- **`xmin` as concurrency token** on Tenant, ClientApplication, User
  ([`AppDbContext.cs:33`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L33)).
  PostgreSQL's hidden row-version column gives optimistic concurrency for free — no
  `RowVersion` column to maintain. `AuthorizationCode` and `RefreshToken` notably do *not*
  get one ([`AppDbContext.cs:92-106`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L92-L106)),
  and that's not cosmetic: two concurrent redemptions of the same code or refresh token can
  both succeed. Doc 06, exercise 11 walks through it.
- **Hashes for everything bearer-shaped.** `AuthorizationCode.CodeHash`,
  `RefreshToken.TokenHash`, `RecoveryCode.CodeHash` — all SHA-256, all uniquely indexed,
  raw value returned to the caller exactly once.
- **`jsonb` for audit payloads**
  ([`AppDbContext.cs:118`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L118))
  — queryable semi-structured data without a column per event type.
- **Domain methods instead of setters.** Every entity mutation is a named method enforcing
  an invariant: [`RefreshToken.MarkReuse`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L273-L277)
  uses `??=` so timestamps are set exactly once;
  [`User.RegisterFailure`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L129-L136)
  couples the counter to the lockout decision. Private constructors exist solely for EF
  materialization. This is "DDD-lite": the entities aren't a full aggregate model (services
  still orchestrate multi-entity transitions), but illegal *single-entity* states are hard
  to construct.

## Decisions and Their Prices

This is the table to be able to reproduce in an interview or design review. For each: what
the repo chose, what it costs, and what would make you switch.

| Decision | Price being paid | Switch signal |
| --- | --- | --- |
| MVC controllers over minimal APIs | attribute ceremony, more files | endpoint count stays tiny and controllers obscure flow — unlikely here |
| Direct `IAppDbContext` over repositories | Application knows EF; harder pure unit tests | repeated query shapes across ≥3 services, or a second data store |
| JWT bearer (in `localStorage`) for the first-party dashboard | XSS steals tokens; revocation waits for expiry | dashboard becomes production-facing → switch to HttpOnly cookie + CSRF defense |
| Permissions embedded in access tokens | permission changes lag until token expiry (15 min) | need real-time revocation → introspection endpoint or shorter tokens |
| Private signing keys as PEM rows in PostgreSQL | DB compromise = token forgery | any real deployment → KMS/HSM or at least encryption-at-rest via Data Protection |
| `EnsureCreated` fallback when no migrations exist | no schema evolution path; drift between environments | immediately — generating the first migration is doc 10, ticket 2 |
| Exceptions (`InvalidOperationException`) as business-error channel | can't distinguish expected from unexpected failures; everything is 400 | OAuth spec compliance work → typed results or an exception hierarchy |
| Audit writes share the request's DbContext and `SaveChangesAsync` ([`AuditWriter.cs`](../src/NetAuth0.Application/Audit/AuditWriter.cs#L14-L19)) | an audit insert flushes *any* pending tracked changes with it; audit failure after a successful operation surfaces as request failure | audit volume grows or audit must never block the operation → outbox/queue |

The senior skill on display here is not knowing which row is "right" — several choices are
perfectly defensible for a learning project and wrong for production. The skill is being
able to fill in the middle column without being told.

## Exercises

1. **Captive dependency hunt.** Suppose someone changes `ITokenIssuer` to a singleton "for
   performance." Trace exactly what breaks, and where the framework would catch it.
2. **Layer violation drill.** You need the requester's IP inside `AdminService`. Write down
   the wrong way (reference `IHttpContextAccessor` from Application) and the way this repo
   does it, and one concrete test that becomes possible with the repo's way.
3. **Constraint backstop.** Find the second place (besides signup) where a service does a
   check-then-act that a unique index backstops. What HTTP response does the user get in the
   race-loss case, and is it acceptable?

<details><summary>Answers</summary>

1. `JwtTokenIssuer` takes `IAppDbContext` (scoped). A singleton issuer would capture one
   DbContext for the process lifetime: concurrent requests share a non-thread-safe context →
   `InvalidOperationException`s under load, stale tracked entities. With
   `ValidateScopes=true` (default in Development) the app throws at startup/first resolve.
2. Repo way: `RequestContext` record registered scoped in
   [`Program.cs:87-94`](../src/NetAuth0.Api/Program.cs#L87-L94). Test: construct
   `new RequestContext("1.2.3.4", "test-agent", null)` and assert the audit row carries the
   IP — no HTTP plumbing required.
3. [`AdminService.AssignRoleAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L138-L142)
   checks `UserRoles.AnyAsync(...)` before inserting; the composite primary key
   `(UserId, RoleId)` ([`AppDbContext.cs:81`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L81))
   is the backstop. (A third instance: `CreateRoleAsync` looks up each permission by name
   before adding it, lines 119-124, backed by the unique `(ClientApplicationId, Name)` index
   at `AppDbContext.cs:76`.) Two admins assigning the same role at the same moment: both
   pass `AnyAsync`, one insert hits the PK, and the resulting `DbUpdateException` becomes a
   generic 400 "Request failed" with no detail outside Development — for an operation whose
   desired end state was actually reached. Acceptable for a lab; in production you'd treat
   that specific violation as success (idempotent assign) or map it to 409.
   (`CreateApplicationAsync`'s in-memory `Distinct` on redirect URIs is *not* an example —
   the app is brand new, so no other request can race it.)

</details>
