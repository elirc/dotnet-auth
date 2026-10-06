# 07 — Testing & Debugging

Goal: know what to test first in *this* repo, write the actual tests, and debug
systematically instead of by vibes. The current test suite is one file with one test
([`SecurityTests`](../tests/NetAuth0.UnitTests/SecurityTests.cs)) protecting an identity
provider — the gap between what this code *does* and what's *proven* is the repo's biggest
liability, and your biggest opportunity to practice.

## The Strategy: Test Where the Risk Lives

Don't aim for coverage; aim for **invariant protection**. Order of value here:

1. **Token lifecycle** (rotation, replay, expiry, single-use codes) — a regression silently
   breaks security, not a page.
2. **Login state machine** (lockout, MFA branching) — six outcomes, zero tests.
3. **PKCE/redirect validation** — each check in the exchange gauntlet deserves one negative
   test, because each check *is* a distinct attack defense.
4. **Tenant scoping of queries** — the class of bug that becomes a breach.
5. UI behavior — last, and mostly via the type system + a few component tests.

The shape mix: mostly **service-level tests against a real database** (EF + PostgreSQL via
Testcontainers), a few **pure unit tests** for crypto helpers and domain methods, a few
**WebApplicationFactory tests** for the HTTP contract (form binding, policies, status
codes). Skip mocking `IAppDbContext` — mocked `DbSet` LINQ lies to you; the SQL translation
*is* part of what you're testing.

## Where Tests Go (and What CI Actually Runs)

Two test projects exist, and they are not equal:

| Project | References | Contents today | Run by CI? |
| --- | --- | --- | --- |
| [`NetAuth0.UnitTests`](../tests/NetAuth0.UnitTests/NetAuth0.UnitTests.csproj) | Domain, Infrastructure, xunit | 1 `[Fact]` ([`SecurityTests.cs:8`](../tests/NetAuth0.UnitTests/SecurityTests.cs#L8)) | yes |
| [`NetAuth0.IntegrationTests`](../tests/NetAuth0.IntegrationTests/NetAuth0.IntegrationTests.csproj) | Api (so everything), `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` | **no `.cs` files at all** | **no** |

CI runs exactly one test command:
`dotnet test tests/NetAuth0.UnitTests/NetAuth0.UnitTests.csproj` (see
[`ci.yml`](../.github/workflows/ci.yml)). So worked example 1 belongs in `UnitTests`, and
examples 2 and 3 belong in `IntegrationTests` because that project already has the packages
they need. But nothing you put in `IntegrationTests` is protected until someone adds a CI
step for it. That step needs Docker on the runner (Testcontainers); `ubuntu-latest` GitHub
runners have it. Adding the CI step is part of the job: a test that no pipeline runs is
documentation, not protection.

## What Makes This Codebase Testable (Use It)

The abstractions exist precisely for this:

- `IClock` — make lockout/expiry tests deterministic: `FakeClock { UtcNow = T0 }`, advance
  15 minutes without `Thread.Sleep`.
- `ISecretGenerator`, `ITotpService`, `IPasswordHasher` — real implementations are fine in
  tests, but BCrypt at work factor 12 costs hundreds of milliseconds per call (it's
  supposed to); for login-heavy suites inject a cheap fake hasher.
- `RequestContext` — `new RequestContext("1.2.3.4", "tests", null)`. No HTTP needed for any
  service test.
- `public partial class Program;` ([`Program.cs:154`](../src/NetAuth0.Api/Program.cs#L154))
  exists specifically so `WebApplicationFactory<Program>` can host the app in-process.

## Worked Example 1: Domain Unit Test (free wins)

Domain methods need no infrastructure at all. Lockout in five minutes:

```csharp
public sealed class UserLockoutTests
{
    [Fact]
    public void Locks_after_max_failures_and_register_failure_after_expiry_relocks()
    {
        var user = new User(Guid.NewGuid(), "a@b.c", "A", "hash");
        var t0 = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++) user.RegisterFailure(t0, 5, TimeSpan.FromMinutes(15));
        Assert.True(user.LockoutEndsAt > t0);                       // locked

        var afterExpiry = t0.AddMinutes(16);
        user.RegisterFailure(afterExpiry, 5, TimeSpan.FromMinutes(15));
        Assert.True(user.LockoutEndsAt > afterExpiry);              // re-locked instantly
    }
}
```

That second assertion documents a real behavior worth knowing: the counter never resets on
lockout expiry ([`RegisterFailure`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L129-L136)),
so **one** post-expiry failure re-locks for another 15 minutes. Maybe intended, maybe not —
the test pins it either way so a future change is a *decision*, not an accident.
[`RefreshToken`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L239-L278) state
transitions deserve the same treatment (`RotateTo` then `MarkReuse`, `Revoke` idempotency
via `??=`, `IsActive` truth table).

## Worked Example 2: The Test This Repo Needs Most

Refresh replay revokes the family — the repo's crown-jewel behavior, currently unproven:

```csharp
public sealed class RefreshRotationTests : IAsyncLifetime
{
    private PostgreSqlContainer _pg = new PostgreSqlBuilder().Build();  // Testcontainers
    private AppDbContext _db = null!;
    private OAuthService _sut = null!;
    // InitializeAsync: start container, create AppDbContext with the container's
    // connection string + EnsureCreated, seed one tenant/app/user,
    // construct OAuthService with real SecretGenerator/BcryptPasswordHasher/
    // JwtTokenIssuer and a FakeClock.

    [Fact]
    public async Task Replaying_rotated_token_revokes_entire_family()
    {
        var ctx = new RequestContext("1.1.1.1", "tests", null);
        var first = await IssueInitialRefreshTokenAsync();           // via code exchange

        var second = await _sut.ExchangeTokenAsync(
            RefreshRequest(first), ctx, default);                    // rotate: ok

        var replay = () => _sut.ExchangeTokenAsync(
            RefreshRequest(first), ctx, default);                    // replay old token
        await Assert.ThrowsAsync<InvalidOperationException>(replay);

        var family = await _db.RefreshTokens.ToListAsync();
        Assert.All(family, t => Assert.NotNull(t.RevokedAt));        // family dead
        Assert.Contains(family, t => t.ReuseDetectedAt != null);

        var useReplacement = () => _sut.ExchangeTokenAsync(
            RefreshRequest(second.RefreshToken!), ctx, default);     // child dead too
        await Assert.ThrowsAsync<InvalidOperationException>(useReplacement);

        Assert.Contains(await _db.AuditEvents.ToListAsync(),
            e => e.EventType == "refresh_token.reuse_detected");
    }
}
```

Notes that matter more than the code:

- **Assert the database, not just the exception.** The contract is "family revoked + audit
  written," not "it threw."
- **The last assertion (replacement dead) is the one juniors forget** — it's the entire
  point of family revocation.
- A second test injecting a throwing `ITokenIssuer` between the two saves pins the
  atomicity bug from doc 06 ex. 8 — write it *expecting current behavior to fail*, mark it
  `[Fact(Skip="known: rotation not transactional — docs2/08 F4")]`, and you've turned a
  finding into an executable TODO. That's a senior-level artifact.
- The same fixture is what doc 06 ex. 11 (parallel redemption) needs. The one change is
  that each concurrent call gets its **own** `AppDbContext` and `OAuthService`, because a
  single context is not thread-safe, and sharing one would hide the race behind EF's
  change tracker.

## Worked Example 3: HTTP-Contract Test

Some behavior only exists at the HTTP layer: form binding names, Basic-auth parsing,
policies, the rate limiter, ProblemDetails shape.

Before writing one, know the three things about *this* `Program.cs` that will make a naive
`WebApplicationFactory<Program>` test fail. Each one comes from reading the code:

1. **Startup touches PostgreSQL unconditionally.** Lines 138-150 run
   `MigrateAsync`/`EnsureCreatedAsync` and the seeder before the host starts, so the factory
   must point `ConnectionStrings:Postgres` at a real database (Testcontainers) before the
   first `CreateClient()`.
2. **JwtBearer fetches its keys over HTTP from itself.** `Authority` and `MetadataAddress`
   are `http://localhost:5080` ([`Program.cs:49-55`](../src/NetAuth0.Api/Program.cs#L49-L55)).
   Under `TestServer` nothing listens on that port, so every bearer token would fail
   validation and you'd see 401 where you expected 403. Route the metadata fetch through the
   in-memory server by setting `JwtBearerOptions.BackchannelHttpHandler`.
3. **The rate limiter partitions by remote IP** ([`Program.cs:73-83`](../src/NetAuth0.Api/Program.cs#L73-L83)),
   and under `TestServer` that's usually null, so every request lands in the `"unknown"`
   bucket. All tests share 20 requests/minute on the `public-auth` endpoints (signup, login,
   token, authorize). A larger suite will start seeing 429s that have nothing to do with the
   code under test.

A factory and two tests that respect all three. This is a sketch written against the
current signatures (`SignupRequest`/`LoginRequest`/`LoginResponse` in
[`IdentityContracts.cs`](../src/NetAuth0.Application/Identity/IdentityContracts.cs)); it was
**not compiled** in this docs pass, so expect to fix `using`s.

```csharp
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().Build();

    public Task InitializeAsync() => _pg.StartAsync();
    public new Task DisposeAsync() => _pg.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _pg.GetConnectionString());   // gotcha 1
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
                o => o.BackchannelHttpHandler = Server.CreateHandler()));          // gotcha 2
    }
}

public sealed class HttpContractTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unsupported_grant_type_returns_400()
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["grant_type"] = "password" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);   // OAuthService.cs:78 throws → handler maps to 400
    }

    [Fact]
    public async Task Admin_endpoint_without_permission_claim_returns_403()
    {
        var client = factory.CreateClient();
        var email = $"noperm-{Guid.NewGuid():N}@test.local";

        // A fresh signup has no UserRole rows, so IssueUserTokensAsync emits no "permissions" claims.
        (await client.PostAsJsonAsync("/api/v1/auth/signup",
            new { tenantSlug = "default", email, displayName = "No Perms", password = "Password123!" }))
            .EnsureSuccessStatusCode();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { tenantSlug = "default", email, password = "Password123!" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var resp = await client.GetAsync(
            $"/api/v1/admin/applications?tenantId={login.GetProperty("tenantId").GetString()}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);   // pins Program.cs:65-71 + AdminController.cs:14
    }
}
```

Why the second test is worth more than it looks: if it returns **401** instead of 403,
gotcha 2 is biting (authentication failed before authorization ran). That's the 401-vs-403
distinction from the debugging playbook below, met in a test instead of an incident. A
natural third test is the tenant-isolation one doc 08 F2 asks for: insert a second tenant
directly through `AppDbContext` (there is no tenant-creation API), log in as the seeded
admin, and call `GET /api/v1/admin/users?tenantId=<other tenant>`.
Today it returns 200. Write it with the assertion you *want* (403) and mark it
`Skip` with the finding ID.

## The Debugging Playbook

The loop, always: **reproduce → bisect the layer → one hypothesis → cheapest probe → root
cause → regression test.** Never fix what you haven't reproduced; never close what you
haven't tested.

Layer bisection for this system means asking, in order: *frontend state? request transport?
authentication? authorization? service logic? SQL? data?* Each has a cheap probe:

| Layer | Probe |
| --- | --- |
| frontend state | React Query devtools; `localStorage` in the console |
| transport | network tab — did the `Authorization` header go out? ([`client.ts:66-79`](../frontend/src/api/client.ts#L66-L79)) |
| authentication | paste the JWT into a decoder; check `iss` vs [`Program.cs:49`](../src/NetAuth0.Api/Program.cs#L49), `exp`, `kid` vs `/.well-known/jwks.json` |
| authorization | decoded `permissions` claims vs the policy on the endpoint |
| service | Serilog request logs; temporary structured log at the suspect branch |
| SQL | `Microsoft.EntityFrameworkCore.Database.Command` at `Information` in `appsettings.json` |
| data | psql: `select * from identity."RefreshTokens" where "FamilyId" = …` |

### Worked scenario: "login succeeds but admin pages 401/403"

The most instructive bug this app can produce, because it can live in five places:

1. 401 vs 403 — *which*? 401 = authentication (token absent/invalid), 403 = authorization
   (valid token, missing claim). This one question halves the search space. (Junior skips
   it; mid asks it; senior asks it *and* knows the repo's catch-all 400 can mask both —
   doc 06 ex. 6.)
2. If 401: is the token in `localStorage`? Note
   [`LoginPage.tsx:29-35`](../frontend/src/pages/LoginPage.tsx#L29-L35) — on
   `requiresMfa=true` no token is stored; you can be "logged in" UI-wise with no token.
3. Still 401: decode it. Wrong `iss` (config drift between `Auth:Issuer` and the JwtBearer
   authority), expired (15-min lifetime, no refresh in the dashboard — doc 04 flow 1), or
   `kid` missing from JWKS (rotated and >7-day window? clock skew?).
4. If 403: decode `permissions`. Empty means the role→permission→claim chain broke — check
   the user's `UserRole` rows against [`DbSeeder`](../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L39-L44),
   then string-compare claims to policies (drift: `users:manage` vs `user:manage`).
5. Root cause found → write the regression test at the layer the bug lived in, not the
   layer it surfaced in.

### Worked scenario: "user reports being logged out of everything"

Hypothesis ranking by prior probability, cheapest probe first: (a) replay detection fired —
query audit for `refresh_token.reuse_detected` (one SQL query, also tells you the IP);
(b) admin force-logout — audit again (`admin.user_force_logout`); (c) all sessions expired
naturally — compare `ExpiresAt`s. The audit table answers all three in one query — *which
is the argument for audit completeness* (and why key rotation's missing event, doc 08 F7,
matters: mass token invalidation from a rotation+JWKS bug would be invisible).

## Habits That Compound

- When a bug took >30 minutes, the lesson is rarely the fix — it's *which probe you should
  have run first*. Write it down.
- Every bug gets a test **that fails before the fix and passes after**. If you can't write
  that test, you haven't actually found the root cause.
- Before committing: re-run the *invariant* tests (token lifecycle), not just the test for
  your change. Security regressions are quiet.

## Exercises

**Exercise 1 (easy) — read the one test that exists.**
*Goal:* know exactly what the current suite proves. Open
[`SecurityTests.cs`](../tests/NetAuth0.UnitTests/SecurityTests.cs) and write one sentence
for each assertion. *Hint:* follow `Sha256Base64Url` and `FixedTimeEquals` into
[`SecurityServices.cs:23-34`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L23-L34).
**Check:** you should conclude that the test proves the hash is deterministic and that
`FixedTimeEquals` distinguishes two different hashes (lines 16-17). It does **not** prove
anything about `OAuthService`'s PKCE branch, the `plain` method, or that the comparison is
actually constant-time. If your sentence says "PKCE is tested," re-read it.

**Exercise 2 (medium) — pin a domain truth table.**
*Goal:* write `RefreshToken.IsActive` tests with no database. *Hint:* the constructor is
public ([`IdentityEntities.cs:242`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L242));
`IsActive` is one line ([line 266](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L266)).
**Check:** your cases must include (a) a fresh unexpired token → `true`; (b) after
`RotateTo` → `false` *and* `RevokedAt` is set, because `RotateTo` sets both fields (lines
267-271), which is exactly what causes doc 06 ex. 7; (c) `Revoke` twice keeps the *first*
timestamp (`??=`, line 272); (d) `ExpiresAt == now` → `false` (strict `>`). If (b) surprises
you, you've found the root of the sessions-list bug by test rather than by reading.

**Exercise 3 (hard) — the red test for a security fix.**
Pick doc 06's exercise 5 (refresh without client secret). Write the full red test against
`OAuthService` — seed a confidential client, get a refresh token legitimately, present it
with `ClientSecret = null`, assert it throws. Run it mentally through the code: confirm it
fails (the exchange succeeds) at [`OAuthService.cs:183-186`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L183-L186).
Then write the one-line fix and confirm the test goes green — and that the *public-client*
refresh test still passes. That pair of tests is a complete, senior-quality PR. Doc 10,
ticket 5 formalizes it.
