# Sealed answer — Incident 001 (clock-skew 401 spike)

**BUILD: UNVERIFIED** — this machine has only the .NET 10 SDK installed;
`global.json` pins `8.0.0` (`rollForward: latestFeature`), so `dotnet build` fails with
"A compatible .NET SDK was not found" before it reaches the compiler. The change is a
single-property addition to an object initializer with no new symbols, so it's very unlikely
to be a compile error, but this was not confirmed by an actual build. If you have a matching
SDK locally, verify with:
`dotnet build src/NetAuth0.Api/NetAuth0.Api.csproj` on branch
`training/incidents/001-clock-skew-401-spike`.

## Root cause

`Program.cs` line ~56-63, diff `1d70eac..563e916`:

```diff
 options.TokenValidationParameters = new TokenValidationParameters
 {
     ValidateIssuer = true,
     ValidIssuer = issuer,
     ValidateAudience = false,
     NameClaimType = ClaimTypes.NameIdentifier,
+    // SEC-482: reject tokens the instant they cross their exp claim, no grace window.
+    ClockSkew = TimeSpan.Zero
 };
```

`Microsoft.IdentityModel.Tokens.TokenValidationParameters.ClockSkew` defaults to **5
minutes** when not set. That default exists specifically because the token issuer (this
API's own process, when it calls `CreateJwtAsync`) and the token validator (the JWT bearer
middleware, potentially on a different pod/instance) don't share a perfectly synchronized
clock — container hosts drift by hundreds of milliseconds to a few seconds under normal NTP
operation, more under load or after a pause (GC, CPU throttling, live migration).

Setting `ClockSkew = TimeSpan.Zero` removes that tolerance. A token is valid for
`AccessTokenLifetimeMinutes` (15 min default, `IdentityEntities.cs:57`) from the moment
`clock.UtcNow` was read on the issuing instance (`JwtTokenIssuer.cs:50`,
`now.AddMinutes(application.AccessTokenLifetimeMinutes)`). If the validating instance's
clock is even ~1 second ahead of the issuing instance's clock at the exact moment a request
lands within that last second of the token's lifetime, `Lifetime validation failed` fires —
which is exactly the `IDX10223` message and the sub-2-second `Current time - ValidTo` gap in
every sampled log line.

This isn't a bug that fires *near* expiry loosely — it fires *only* in that last ~clock-drift
window before natural expiry, which is also exactly when a healthy client is statistically
most likely to be mid-request (uniform token lifetime distribution across a live session).
That's why the failure rate is a clean step function proportional to traffic, not correlated
with any particular tenant, client, or endpoint — every protected endpoint sees the same
population of "requests issued with a nearly-expired token."

## Diagnostic path a senior engineer takes

1. Metrics: step-change exactly at deploy time, flat afterward (not decaying) rules out a
   transient cache/warm-up issue and points at a config/code change in that deploy, not an
   external dependency.
2. Logs: the `IDX10223` message plus `ValidTo`/`Current time` being within ~1-2s of each
   other, every single time — this is the tell. If this were a broader auth bug (bad
   audience, bad issuer, bad signature), the error message and evidence would differ
   (`IDX10214`/`IDX10501`/etc.), not consistently a *lifetime* failure at the boundary.
3. Correlate to the single PR in the deploy; `git diff` on `Program.cs` shows a
   `TokenValidationParameters` change touching exactly this.
4. Confirm by checking wall-clock skew between API instances (`chronyc tracking` / cloud
   provider's NTP metric) — even sub-second skew is enough to reproduce, so this step mostly
   confirms magnitude, not presence.

## Fix

Revert to the framework default (remove the explicit `ClockSkew = TimeSpan.Zero`), or set an
explicit, intentional value if SEC-482 wanted a *smaller* window than 5 minutes for a
documented reason — e.g. `ClockSkew = TimeSpan.FromSeconds(30)` is a defensible hardening if
NTP-synced infra is guaranteed and the goal is closing a multi-minute exploitation window,
but `TimeSpan.Zero` assumes clocks are perfectly synchronized, which is never a safe
assumption in a distributed deployment. If SEC-482's actual intent was "access tokens should
be short-lived," the correct lever is `AccessTokenLifetimeMinutes`
(`ClientApplication.cs`/`IdentityEntities.cs:57`), not `ClockSkew`.

## Regression test

An integration test against the `JwtBearer` pipeline (in
`tests/NetAuth0.IntegrationTests`) that:
1. Mints a token with a fake `IClock` frozen at `T`.
2. Sends an authenticated request to a protected endpoint with a validating clock frozen at
   `T + AccessTokenLifetimeMinutes + 2 seconds` (simulating benign clock drift, not real
   expiry).
3. Asserts the request still succeeds (proves a nonzero skew tolerance is configured) — then
   a second assertion with the validating clock frozen well past the skew window (e.g. `T +
   lifetime + 10 minutes`) asserts 401, proving expiry is still enforced, just not
   instantaneously.

This is the kind of test that's cheap to write and would have caught this in code review,
not production — which is itself worth a line in the postmortem.

## Postmortem (model)

*Impact:* ~6% of requests to protected endpoints across all tenants returned spurious 401s
for the duration between the 14:10 UTC deploy and the fix, self-resolving per-request on
retry/refresh. No data was exposed or lost; this was an availability regression, not a
security one. *Trigger:* PR "security: tighten JWT validation window per SEC-482" set
`ClockSkew = TimeSpan.Zero` on the JWT bearer validation parameters, removing the default
5-minute tolerance for clock drift between the token-issuing and token-validating processes.
*Why it wasn't caught pre-deploy:* the change was reviewed as a security hardening with an
obviously-good intent ("don't accept stale tokens") and no test exercised the boundary
behavior of `ClockSkew` itself — only tokens well inside or well outside their lifetime were
ever tested. *Fix:* reverted to framework-default skew tolerance. *Follow-up:* add the
regression test above; add a lint/review checklist item that any change to
`TokenValidationParameters` needs an explicit boundary test, since every field on that type
is a plausible "small hardening PR that breaks distributed systems in a way unit tests
running on one clock can't see."
