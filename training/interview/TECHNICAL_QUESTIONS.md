# Technical questions — NetAuth0

All 20 questions here reference this repo directly (this project's DRILL kit only requires
10; the rest are here so you have room to pick the ones your target role weights most).
Sealed model answers are not provided for these — the review, both incidents, and their
sealed answers already give you the evidence trail; use it to build your own answer, then
defend it out loud (or against `training/agentic/` if you build that kit later).

## API design / OAuth

1. Walk through what happens, file by file, from a `GET /connect/authorize` request to the
   browser receiving a redirect with a `code`. Where exactly does PKCE get bound to the
   authorization code, and what stops a `code` issued for one PKCE challenge from being
   redeemed with a different `code_verifier`? (`OAuthController.cs`, `OAuthService.cs:25-69`,
   `IdentityEntities.cs:204-237`)
2. `OAuthController.cs:22` binds `user_id` straight from the query string on
   `/connect/authorize`. What's the actual authentication mechanism a production
   authorize endpoint needs instead, and why does adding it change more than just that one
   controller action? (Hint: think about what `/connect/authorize` currently *doesn't* need
   to do — like check for an existing session cookie — and what it would.)
3. Why does `token_endpoint_auth_methods_supported` in `OidcController.cs:25` list `"none"`
   as valid, and which client type in this codebase (`ClientApplicationType`,
   `IdentityEntities.cs:5-9`) is that for? What would go wrong if a confidential client used
   `"none"`?

## Token validation / sessions

4. `Program.cs:56-62` sets `ValidateAudience = false`. Trace exactly how that interacts with
   `AdminController`'s tenant-trusting-from-query-string bug (`AdminService.cs:30-33`) to
   produce finding #2 in `training/_answers/review.md`. If you could only ship one of "turn
   audience validation on" or "check tenant_id claim in AdminService," which closes more of
   the actual exposure, and why?
5. Explain `ClockSkew` from first principles (not "what fixed incident 001," the mechanism):
   why does a distributed JWT-issuing/validating system need a tolerance window at all, what
   determines the right size for one, and what's the failure mode on *each* side (too large
   vs. `TimeSpan.Zero`) of getting it wrong?
6. `GetSessionsAsync` in both `OAuthService.cs:82-89` and `IdentityService.cs:126-133` filter
   on `RotatedFromId == null` to list "sessions." What does a refresh-token family actually
   represent here, and why does filtering on that column (rather than, say, `RevokedAt ==
   null`) give you "one row per logical session" instead of "one row per token"?

## Refresh tokens / concurrency

7. Walk through `ExchangeRefreshTokenAsync` (`OAuthService.cs:175-230`) end to end. At which
   exact line does the old refresh token get marked consumed, and what would happen if the
   process crashed one line before that, after the replacement token was already committed?
   (This is finding #4 in the sealed review — answer the mechanism, not just cite the rank.)
8. What is "refresh token reuse detection" and where is it implemented
   (`OAuthService.cs:199-203`, `RevokeFamilyForReuseAsync`, `RefreshToken.MarkReuse`)? Design
   an attack scenario where an attacker steals a refresh token, and explain step by step what
   the legitimate user's *next* refresh call does and why the whole family gets revoked
   instead of just the stolen token.
9. `OAuthService.cs:183-186` only validates the client secret on the refresh grant if the
   request includes one. Why is that a bug rather than a reasonable "public clients don't
   have secrets" accommodation — what's different between "this client type has no secret"
   and "this specific request omitted the secret field"?

## Security / OWASP ASVS

10. Pick any 3 ASVS-tagged findings from `training/_answers/review.md` and explain, for each,
    which of the four review passes (trust-boundary / asset / state-machine / negative-space,
    per `docs2/08-security-review-walkthrough.md`) would surface it, and why that pass and not
    another.
11. `SecurityServices.cs:29-34` uses `CryptographicOperations.FixedTimeEquals` for PKCE
    verifier comparison. What timing attack does this defend against concretely, and why does
    the length check on line 33 (`leftBytes.Length == rightBytes.Length`) happen *before* the
    constant-time comparison without reintroducing a timing leak?
12. Recovery codes are generated and hashed (`IdentityService.cs:104-111`,
    `RecoveryCode` entity) but never redeemed anywhere in the codebase. Design the missing
    redemption path: where does it hook into `LoginAsync`'s MFA branch, what has to be true
    for a recovery code to be accepted, and what has to happen to it afterward so it can't be
    reused?
13. The TOTP verification path (`IdentityService.cs:80-84`, `TotpService.Verify`,
    `SecurityServices.cs:46-50`) has no per-account failure counter, unlike password login
    (`RegisterFailure`, `IdentityEntities.cs:129-136`). Design the fix: what counter, keyed on
    what, resets when, and does it share the existing `LockoutEndsAt`/`FailedAccessCount`
    fields or need its own?

## Data modeling / multi-tenancy

14. Every major entity (`User`, `ClientApplication`, `RefreshToken`, `AuditEvent`, etc.) has a
    `TenantId`. Find every place in `AdminService.cs` where `tenantId` is accepted as an
    input rather than derived from the authenticated principal, and describe the one-pattern
    fix mentioned in `training/_answers/review.md`'s "Fixes, Ordered by Leverage" §1.
15. Why does `SignupAsync` (`IdentityService.cs:31-47`) look up the tenant by `Slug` and
    reject inactive tenants, but nothing in the OAuth flows (`OAuthService.cs`) re-checks
    `Tenant.IsActive` before issuing tokens? Is that a gap, and if so how would you close it
    without adding a query to every token issuance?

## Testing / operability

16. `tests/NetAuth0.UnitTests/SecurityTests.cs` has exactly one test, covering PKCE hashing.
    Given the findings in `training/_answers/review.md`, write down (don't implement) the
    three highest-value tests you'd add first, and justify the ordering.
17. `Program.cs:138-150` falls back to `EnsureCreatedAsync()` when there are no committed
    migrations. What operational trap does this leave for the first real schema change made
    against a live database, and what's the one-time fix?
18. The rate limiter in `Program.cs:73-83` partitions on `context.Connection.RemoteIpAddress`.
    Why does that break down specifically once this API sits behind a load balancer or
    reverse proxy, and what ASP.NET Core feature closes that gap safely (i.e. without letting
    a client spoof its own rate-limit partition via a forged header)?
19. `JwtTokenIssuer.GetCurrentSigningKeyAsync` (`JwtTokenIssuer.cs:112-130`) lazily creates a
    signing key on first use if none exists. In a horizontally-scaled deployment, what race
    condition does this create if two API instances both cold-start with an empty
    `SigningKeys` table at the same time, and what would each of them end up signing tokens
    with?
20. `KeyManagementService`/`AdminController.cs`'s `signing-keys/rotate` endpoint rotates a
    key but the audit writer is never called from that path. Why does "who rotated the
    signing key, and when" matter more for an IdP than almost any other audit event this
    system logs, and what's the one-line fix?
