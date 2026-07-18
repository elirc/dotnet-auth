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

## What Makes This Codebase Testable (Use It)

The abstractions exist precisely for this:

- `IClock` — make lockout/expiry tests deterministic: `FakeClock { UtcNow = T0 }`, advance
  15 minutes without `Thread.Sleep`.
- `ISecretGenerator`, `ITotpService`, `IPasswordHasher` — real implementations are fine in
  tests (fast enough), but BCrypt at work factor 12 is ~100ms/call; for login-heavy suites
  inject a cheap fake hasher.
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
  `[Fact(Skip="known: rotation not transactional — doc2/08 F4")]`, and you've turned a
  finding into an executable TODO. That's a senior-level artifact.

## Worked Example 3: HTTP-Contract Test

Some behavior only exists at the HTTP layer: form binding names, Basic-auth parsing,
policies, the rate limiter, ProblemDetails shape.

```csharp
public sealed class TokenEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Unsupported_grant_type_returns_400()
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["grant_type"] = "password" }));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_endpoint_without_permission_claim_returns_403()
    {
        // issue a token with no "permissions" claims, call /api/v1/admin/applications
        // expect 403 — this test pins the policy wiring in Program.cs:65-71
    }
}
```

(You'll need to override the connection string for tests —
`WebApplicationFactory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(...))` pointing
at the Testcontainers instance. The startup seeding at
[`Program.cs:138-150`](../src/NetAuth0.Api/Program.cs#L138-L150) then gives you the default
tenant/admin for free.)

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

## Exercise

Pick doc 06's exercise 5 (refresh without client secret). Write the full red test against
`OAuthService` — seed a confidential client, get a refresh token legitimately, present it
with `ClientSecret = null`, assert it throws. Run it mentally through the code: confirm it
fails (the exchange succeeds) at [`OAuthService.cs:183-186`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L183-L186).
Then write the one-line fix and confirm the test goes green — and that the *public-client*
refresh test still passes. That pair of tests is a complete, senior-quality PR. Doc 10,
ticket 5 formalizes it.
