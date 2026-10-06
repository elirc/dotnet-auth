# 01 — Orientation

Goal: in half a day, know what this system is, what every directory is for, the vocabulary,
and how to run what can be run. Don't try to understand the auth logic yet — that's docs 03–04.

## What This Product Is

NetAuth0 is a **standalone identity provider** — a small Auth0/Okta clone. Other applications
("clients") outsource login to it. It answers three questions for them:

1. **Who is this user?** — signup, login, MFA ([`IdentityService`](../src/NetAuth0.Application/Identity/IdentityService.cs))
2. **Can this client get tokens for this user?** — OAuth2/OIDC flows ([`OAuthService`](../src/NetAuth0.Application/OAuth/OAuthService.cs))
3. **What is this user allowed to do?** — roles and permissions embedded in JWTs ([`JwtTokenIssuer`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs))

It also ships an admin dashboard (React SPA) for registering client applications, managing
users, and reading the audit log.

## System Map

```
┌─────────────────────────┐        ┌──────────────────────────────────────────┐
│  frontend/ (React SPA)  │        │  src/ (.NET 8 Web API)                   │
│  Vite + TS + TanStack   │ HTTP   │                                          │
│  Query + RHF + zod      ├───────►│  NetAuth0.Api          ── controllers,   │
│                         │ :5080  │   │                       Program.cs     │
│  localhost:5173         │        │   ▼ depends on                           │
└─────────────────────────┘        │  NetAuth0.Application  ── services,      │
                                   │   │                       contracts,     │
   Other OAuth clients ───────────►│   │                       abstractions   │
   (/connect/authorize,            │   ▼ depends on                           │
    /connect/token,                │  NetAuth0.Domain       ── entities,      │
    /.well-known/*)                │   ▲                       invariants     │
                                   │   │ implemented by                       │
                                   │  NetAuth0.Infrastructure ─ EF Core,      │
                                   │       │                    crypto, JWT   │
                                   └───────┼──────────────────────────────────┘
                                           ▼
                                   PostgreSQL (docker-compose) + Redis (unused so far)
```

Dependency rule: **Api → Application → Domain**, and **Infrastructure implements Application's
interfaces**. Application never references Infrastructure; the wiring happens in
[`DependencyInjection.AddInfrastructure`](../src/NetAuth0.Infrastructure/DependencyInjection.cs#L16-L35),
which the API calls at startup ([`Program.cs:21`](../src/NetAuth0.Api/Program.cs#L21)).

## Directory Tour

| Path | What lives there | Read it when |
| --- | --- | --- |
| `src/NetAuth0.Api/Controllers/` | 4 controllers: [`AuthController`](../src/NetAuth0.Api/Controllers/AuthController.cs) (signup/login/MFA/sessions), [`OAuthController`](../src/NetAuth0.Api/Controllers/OAuthController.cs) (authorize/token), [`OidcController`](../src/NetAuth0.Api/Controllers/OidcController.cs) (discovery/JWKS), [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs) (admin APIs) | mapping HTTP to services |
| `src/NetAuth0.Api/Program.cs` | All startup: DI, auth, CORS, rate limiting, error handling, schema creation, seeding | first, and again in doc 02 |
| `src/NetAuth0.Application/` | Business logic. One folder per area (Identity, OAuth, Admin, Audit) with a `*Service.cs` and a `*Contracts.cs` (request/response records) | most of your time |
| `src/NetAuth0.Application/Abstractions/` | Interfaces Infrastructure must implement: [`IAppDbContext`](../src/NetAuth0.Application/Abstractions/IAppDbContext.cs), [`SecurityAbstractions`](../src/NetAuth0.Application/Abstractions/SecurityAbstractions.cs) (`IClock`, `IPasswordHasher`, `ISecretGenerator`, `ITokenIssuer`, `ITotpService`) | understanding the seam between layers |
| `src/NetAuth0.Domain/Identity/` | All 15 entity classes (plus the `ClientApplicationType` and `GrantType` enums) in one file: [`IdentityEntities.cs`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs) | learning the nouns |
| `src/NetAuth0.Infrastructure/Persistence/` | [`AppDbContext`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs) (EF mappings), [`DbSeeder`](../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs) (default tenant/admin/SPA client) | schema questions |
| `src/NetAuth0.Infrastructure/Security/` | [`JwtTokenIssuer`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs), [`KeyManagementService`](../src/NetAuth0.Infrastructure/Security/KeyManagementService.cs), [`SecurityServices`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs) (BCrypt, SHA-256, TOTP, randomness) | doc 03 |
| `frontend/src/api/client.ts` | The entire frontend↔API contract: types, token storage, fetch wrapper | frontend work |
| `frontend/src/pages/` | One page per screen; [`ApplicationsPage`](../frontend/src/pages/ApplicationsPage.tsx) is the richest example | frontend work |
| `tests/NetAuth0.UnitTests/` | Currently **one test** (a single `[Fact]` in [`SecurityTests`](../tests/NetAuth0.UnitTests/SecurityTests.cs)). This is a gap, and doc 07 is about fixing it | doc 07 |
| `tests/NetAuth0.IntegrationTests/` | **Only a `.csproj`** — it already references `Microsoft.AspNetCore.Mvc.Testing` and `Testcontainers.PostgreSql`, but contains zero test files, and CI never runs it | doc 07 |
| `docker-compose.yml`, `infra/` | PostgreSQL 16 + Redis containers | running locally |
| `.github/workflows/ci.yml` | Backend build/test + frontend build | knowing what "green" means |

## Domain Glossary

Learn these as *relationships*, not flashcards. All are defined in
[`IdentityEntities.cs`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs).

- **Tenant** — an isolated customer space. Users and applications belong to exactly one
  tenant. The seeded tenant slug is `default`.
- **ClientApplication** — an app that uses NetAuth0 for login (a SPA, web app, or
  machine-to-machine service). Identified by `ClientId`; confidential clients also hold a
  BCrypt-hashed `ClientSecretHash`. SPAs get no secret
  ([`AdminService.cs:36`](../src/NetAuth0.Application/Admin/AdminService.cs#L36)).
- **User** — a person within a tenant. Email is normalized to lowercase at construction
  ([`IdentityEntities.cs:108`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L108)).
- **Role / Permission** — roles are per-application bundles of permission strings (e.g.
  `users:manage`). Permissions end up as `permissions` claims inside access tokens.
- **GrantType** — which OAuth flows a client may use: `AuthorizationCode`,
  `ClientCredentials`, `RefreshToken`.
- **AuthorizationCode** — a 5-minute, single-use code minted at `/connect/authorize` and
  exchanged for tokens at `/connect/token`. Stored **hashed**, never raw.
- **Refresh token family** — every refresh token belongs to a `FamilyId`. Rotation creates a
  child token in the same family; presenting a dead token nukes the whole family. This is
  the repo's most interesting mechanism — doc 03 covers why.
- **SigningKey** — an RSA private key (PEM, in the database) used to sign JWTs. The public
  half is served at `/.well-known/jwks.json` keyed by `kid`.
- **AuditEvent** — append-only security log: who did what, from which IP, with a `jsonb`
  data payload.
- **MfaEnrollment / RecoveryCode** — TOTP secret per user, plus ten one-time recovery codes
  (stored hashed).

## Run It

```powershell
docker compose up -d                      # PostgreSQL :5432, Redis :6379
dotnet run --project src/NetAuth0.Api     # API on http://localhost:5080
cd frontend; npm install; npm run dev     # SPA on http://localhost:5173
```

Login: tenant `default` / `admin@netauth0.local` / `ChangeMe123!` (seeded by
[`DbSeeder`](../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L16-L21)).

Things worth knowing before your first run:

- On startup the API runs migrations **if any exist**, otherwise falls back to
  `EnsureCreatedAsync` ([`Program.cs:138-150`](../src/NetAuth0.Api/Program.cs#L138-L150)).
  No migrations are committed yet, so you're on the fallback path. That's fine locally and
  a real problem for production — doc 08 explains why.
- Swagger UI is at `/swagger` in Development. Health check at `/healthz`.
- The dashboard reads the tenant ID from `localStorage`, which the login page fills in from
  the login response ([`LoginPage.tsx:36`](../frontend/src/pages/LoginPage.tsx#L36)). The
  "Tenant ID" box ([`DashboardPage.tsx:18`](../frontend/src/pages/DashboardPage.tsx#L18)) is
  a manual override; to find a tenant ID by hand, query `identity."Tenants"` in PostgreSQL.

## First Reading Order

Ten files, in order. Skim for shape, not mastery — total budget two hours.

1. [`Program.cs`](../src/NetAuth0.Api/Program.cs) — everything the app *is* at startup.
2. [`IdentityEntities.cs`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs) — the nouns. Notice every property has a private setter and changes go through named methods.
3. [`AppDbContext.cs`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs) — uniqueness, indexes, `xmin` concurrency tokens (and which three entities have one; doc 06 ex. 11 is about the ones that don't).
4. [`DependencyInjection.cs`](../src/NetAuth0.Infrastructure/DependencyInjection.cs) — note which services are singleton vs scoped, and ask yourself why for each.
5. [`OAuthController.cs`](../src/NetAuth0.Api/Controllers/OAuthController.cs) — small, but look at `ReadClientCredentials` handling Basic auth.
6. [`OAuthService.cs`](../src/NetAuth0.Application/OAuth/OAuthService.cs) — the heart of the repo. You will read this file ten more times.
7. [`JwtTokenIssuer.cs`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs) — claims in, signed JWT out, JWKS.
8. [`IdentityService.cs`](../src/NetAuth0.Application/Identity/IdentityService.cs) — login with lockout and MFA branching.
9. [`client.ts`](../frontend/src/api/client.ts) — the entire frontend API surface in 100 lines.
10. [`ApplicationsPage.tsx`](../frontend/src/pages/ApplicationsPage.tsx) — query + mutation + cache invalidation in one screen.

## Checkpoint (do before doc 02)

Answer from memory, then verify in code:

1. Which layer is allowed to reference EF Core types, and which interface lets Application
   run queries anyway?
2. A user, a role, and a permission — which of the three is scoped to a client application?
3. Where does the seeded admin's `applications:manage` permission physically travel from
   database to an authorized admin request? (Name the three hops.)
4. Why does `RefreshToken` store `TokenHash` instead of the token?

<details><summary>Answers</summary>

1. Only Infrastructure references EF concretely, but Application uses
   [`IAppDbContext`](../src/NetAuth0.Application/Abstractions/IAppDbContext.cs), which
   exposes `DbSet<T>` — so Application is *coupled to EF abstractions* by design. Doc 02
   discusses whether that's a good trade.
2. Both roles and permissions are per-application (`ClientApplicationId` on both). Users are
   per-tenant.
3. Seed: `RolePermission` rows ([`DbSeeder.cs:39-44`](../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L39-L44))
   → token issuance queries them into `permissions` claims
   ([`JwtTokenIssuer.cs:19-34`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L19-L34))
   → the policy requires the claim
   ([`Program.cs:65-71`](../src/NetAuth0.Api/Program.cs#L65-L71)).
4. The database is not trusted with bearer secrets: a DB leak of hashes can't be replayed as
   tokens. Same reason auth codes and recovery codes store SHA-256 hashes.

</details>
