# 12 — Reference

Lookup material. Nothing here teaches; it saves round trips while you work through docs
01–11.

## Commands

| Task | Command | Notes |
| --- | --- | --- |
| Infra up | `docker compose up -d` | PostgreSQL :5432 (`netauth0`/`netauth0`), Redis :6379 |
| API | `dotnet run --project src/NetAuth0.Api` | http://localhost:5080, Swagger at `/swagger` (dev) |
| Backend build | `dotnet build src/NetAuth0.Api/NetAuth0.Api.csproj` | what CI runs; there is **no `.sln`** in the repo, so a bare `dotnet build`/`dotnet test` at the root has no project to pick |
| Backend tests | `dotnet test tests/NetAuth0.UnitTests/NetAuth0.UnitTests.csproj` | what CI runs; currently 1 `[Fact]`. `tests/NetAuth0.IntegrationTests` has no test files and is not run by CI — see doc 07 |
| Add migration | `dotnet ef migrations add <Name> -p src/NetAuth0.Infrastructure -s src/NetAuth0.Api` | needs `dotnet-ef` tool; see `infra/postgres/README.md` |
| Frontend dev | `cd frontend; npm install; npm run dev` | http://localhost:5173 |
| Frontend build | `cd frontend; npm run build` | tsc + vite; what CI runs |
| DB shell | `docker compose exec postgres psql -U netauth0` | schema is `identity`, tables are quoted PascalCase |

Seeded login: tenant `default` / `admin@netauth0.local` / `ChangeMe123!`.

## Endpoint Map

| Endpoint | Auth | Handler |
| --- | --- | --- |
| `GET /.well-known/openid-configuration` | none | [`OidcController.Discovery`](../src/NetAuth0.Api/Controllers/OidcController.cs#L9-L27) |
| `GET /.well-known/jwks.json` | none | [`OidcController.Jwks`](../src/NetAuth0.Api/Controllers/OidcController.cs#L29-L33) |
| `GET /connect/authorize` | none (⚠ F1) | [`OAuthController.Authorize`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L11-L32) |
| `POST /connect/token` | client creds in form/Basic | [`OAuthController.Token`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L34-L59) |
| `POST /api/v1/auth/signup` | none, rate-limited | [`AuthController.Signup`](../src/NetAuth0.Api/Controllers/AuthController.cs#L14-L20) |
| `POST /api/v1/auth/login` | none, rate-limited | [`AuthController.Login`](../src/NetAuth0.Api/Controllers/AuthController.cs#L22-L27) |
| `POST /api/v1/auth/mfa/totp` (+`/verify`) | bearer | [`AuthController`](../src/NetAuth0.Api/Controllers/AuthController.cs#L29-L42) |
| `GET/DELETE /api/v1/auth/sessions[/{familyId}]` | bearer | [`AuthController`](../src/NetAuth0.Api/Controllers/AuthController.cs#L44-L57) |
| `GET/POST /api/v1/admin/applications` | `applications:manage` | [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L13-L25) |
| `GET /api/v1/admin/users`, `POST …/block`, `POST …/force-logout` | `users:manage` | [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L27-L48) |
| `POST /api/v1/admin/roles`, `POST …/roles/assignments` | `roles:manage` | [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L50-L63) |
| `GET /api/v1/admin/audit-events` | `audit:read` | [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L65-L70) |
| `POST /api/v1/admin/signing-keys/rotate` | `applications:manage` | [`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs#L72-L77) |
| `GET /healthz` | none | health checks ([`Program.cs:135`](../src/NetAuth0.Api/Program.cs#L135)) |

## Audit Event Taxonomy

`user.signup` · `login.success` · `login.failure` · `login.blocked` · `mfa.challenge` ·
`mfa.failure` · `authorization_code.issued` · `token.issued` · `refresh_token.rotated` ·
`refresh_token.reuse_detected` · `refresh_token.family_revoked` ·
`admin.application_created` · `admin.user_blocked` / `admin.user_unblocked` ·
`admin.user_force_logout` · `admin.role_created` · `admin.role_assigned`

Known gaps: no event for key rotation (doc 10, ticket 1), MFA enrollment, or signup
failure.

## Risk Register (condensed from doc 08)

| ID | Risk | Severity | Fix vehicle |
| --- | --- | --- | --- |
| F1 | authorize trusts `user_id` from query | Critical | doc 10, Project B |
| F2 | no tenant isolation on admin APIs; audience unvalidated | Critical (multi-tenant) | doc 10, Project A |
| F3 | TOTP brute-forceable (no lockout, no step-replay rejection) | High | doc 10, ticket 8 |
| F4 | refresh rotation non-atomic | High | doc 10, ticket 6 |
| F5 | confidential refresh works without client secret | High | doc 10, ticket 5 |
| F6 | recovery codes unredeemable | High (availability) | doc 10, ticket 9 |
| F7 | plaintext signing keys in DB; rotation unaudited | High | doc 10, Project D + ticket 1 |
| F8 | MFA enrollment verification IDOR | Medium | with Project A |
| F9 | tokens in localStorage | Medium | Project B (cookie session) |
| F10 | all errors → 400; non-spec OAuth errors | Medium | doc 10, ticket 7 |
| F11 | login timing oracle (BCrypt skip on unknown user) | Low-Med | dummy-hash verify on unknown user |
| F12 | rate limiter keyed on raw IP, no forwarded headers | Medium (deploy) | productionization |
| F13 | `plain` PKCE accepted | Low | doc 10, ticket 4 |
| F14 | `EnsureCreated` fallback, no migrations | Medium (ops) | doc 10, ticket 2 |
| F15 | codes/refresh tokens single-use only sequentially (no concurrency token) | Medium-High | after ticket 2; see doc 10 ticket 6 scope note |
| F16 | blocking a user doesn't stop refresh | High | doc 10, ticket 10 |
| F17 | token `typ`/audience unchecked; ID tokens accepted as bearer | Low | with Project A |

Plus non-security debt: sessions list shows rotated families as revoked (doc 06 ex. 7);
no token-table cleanup or `UserId` index (doc 09); `RequireConsent` and `RequirePkce=false`
are decorative flags; `LoginTicket` is dead API surface; test suite is one test.

## Level Rubric (self-grade quarterly)

| Dimension | Junior | Mid | Senior |
| --- | --- | --- | --- |
| Reading | follows happy path | traces all branches + side effects | reads for invariants & absences (doc 06 ≥7/12 unaided) |
| Change | works on my machine | red test first, convention-consistent, risk-noted | smallest diff that fixes the *class*; switch signals documented |
| Debugging | print + guess | layer bisection, cheapest probe first | builds the observability that makes the next bug shallow |
| Design | picks the familiar | costs 2–3 options honestly | names non-goals, rollback asymmetries, and what evidence would change the call |
| Review | style & typos | conventions & tests | system invariants & one-way doors |
| Communication | describes the diff | problem/approach/risk/evidence | writes the doc that ends the debate |

## Verification Status of These Docs

- All backend/frontend code citations: verified by reading the source at writing time
  (June 2026). Line anchors drift with edits — trust symbol names over line numbers.
- Frontend `npm install` / `npm run build`: previously verified on this machine.
- Backend commands: **not** locally executed (`dotnet` SDK absent at writing time);
  derived from `.github/workflows/ci.yml`, `global.json` (SDK 8.0.x), and project files.
- Findings F1–F14: established by source analysis. The two with a runtime dependency are
  flagged inline: F-ex.9's model-binding claim (confirm `code_challenge` omission → 400
  with one request) and JwtBearer JWKS cache behavior in doc 03's checkpoint.
- Docker compose, EF migration commands: consistent with `docker-compose.yml` and
  `infra/postgres/README.md`, not executed during the docs pass.
- **2026-10-06 refinement pass** (static, no `dotnet` run): every `path#Lx-Ly` link and
  `File.cs:N` citation in docs2/ was script-checked against the tree at `0f8c17c`, and all
  resolve. Test count re-counted by attribute: 1 `[Fact]`, 0 `[Theory]` across `tests/`.
  Corrected: entity count (15, not 14), the build/test commands (no `.sln`), doc 06 drill
  C's reasoning (Application *does* reference EF Core), doc 02's constraint-backstop
  answer, doc 09's row-growth estimate (dashboard login creates no refresh tokens). Added:
  F15–F17, gym exercises 11–12, ticket 10, and a compile-unverified HTTP-test sketch in
  doc 07.
