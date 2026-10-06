# 10 — Contribution Ladder

Goal: convert everything from docs 01–09 into shipped work. The repo is synthetic, so
treat each ticket as if a maintainer wrote it: small blast radius, tests required, risk
notes in the PR description (doc 11 covers how to write those). Work in a branch per
ticket; the CI definition of green is `.github/workflows/ci.yml` (backend build+test,
frontend build).

Each rung assumes the previous one felt comfortable. Don't skip — the point of the early
tickets is calibrating *process* (tests, PR writing) on low-risk changes before touching
token code.

---

## Rung 1 — First PRs (junior, calibration)

### Ticket 1: Audit event for signing-key rotation
Severity-1 traceability gap (doc 08, F7-adjacent), trivially small change.

- **Change:** [`KeyManagementService.RotateAsync`](../src/NetAuth0.Infrastructure/Security/KeyManagementService.cs#L10-L23)
  (or the controller) emits `signing_key.rotated` with the new key ID, retired key IDs, and
  `RequestContext` actor/IP.
- **Accept:** rotation writes exactly one event; JWKS behavior unchanged; a test asserts
  the event exists and **does not contain key material**.
- **Trap:** `KeyManagementService` doesn't currently take `IAuditWriter` or
  `RequestContext` — decide whether the audit belongs in Infrastructure or in a thin
  Application wrapper, and defend the choice in the PR. (Hint: every other audit lives in
  Application.)

### Ticket 2: Commit the initial EF migration
Removes the `EnsureCreated` trap (doc 08, F14; doc 06, drill D).

- **Change:** `dotnet ef migrations add Initial -p src/NetAuth0.Infrastructure -s src/NetAuth0.Api`,
  commit, verify startup takes the `MigrateAsync` branch.
- **Accept:** fresh database boots via migrations; CI still green; a README note explains
  the migration workflow.
- **Trap:** an *existing* dev database created by `EnsureCreated` has no
  `__EFMigrationsHistory` — document the "drop and recreate dev DB" step or the first run
  fails confusingly.

### Ticket 3: Centralize permission constants
Kills the string-drift failure mode (doc 05, pattern 6).

- **Change:** one constants class in **Application** (not Infrastructure — dependency
  direction); replace literals in [`Program.cs:67`](../src/NetAuth0.Api/Program.cs#L67),
  [`DbSeeder.cs:31-37`](../src/NetAuth0.Infrastructure/Persistence/DbSeeder.cs#L31-L37),
  and controller attributes (attribute arguments need `const`).
- **Accept:** zero remaining raw permission literals (`grep` proves it); behavior identical.

### Ticket 4: Reject `plain` PKCE
- **Change:** [`ValidatePkce`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L256-L267)
  and the exchange's challenge switch accept only `S256`; update the discovery document's
  `code_challenge_methods_supported` ([`OidcController.cs:23`](../src/NetAuth0.Api/Controllers/OidcController.cs#L23)).
- **Accept:** `plain` requests fail at *both* authorize and exchange; discovery tells the
  truth; tests for both rejections.
- **Trap:** the discovery doc is the contract clients configure from — changing behavior
  without it is how you break integrators. That's the actual lesson of this ticket.

---

## Rung 2 — Mid-level features (the bar you're training for)

### Ticket 5: Fix the unauthenticated-refresh hole **(do this one first)**
Doc 06 ex. 5 / doc 08 F5. Tiny diff, real security value, full test ritual.

- **Accept:** confidential-client refresh without secret → error; with wrong secret →
  error; public client (no `ClientSecretHash`) unaffected. Red test written *before* the
  fix (doc 07's exercise is this ticket's test plan).

### Ticket 6: Make refresh rotation atomic
Doc 08 F4. The senior-flavored part is *choosing* the mechanism.

- **Change:** single commit for {child insert, parent `RotateTo`} — either remove the
  early `SaveChangesAsync` or wrap in an explicit transaction. Mind the hidden save inside
  token issuance (key bootstrap).
- **Accept:** fault-injection test (throwing `ITokenIssuer`) shows the old token still
  sole-active after a mid-rotation crash; replay test from doc 07 still green.
- **PR must answer:** why you chose single-save vs explicit transaction, and what happens
  to the audit write's separate commit in each design.
- **Scope note:** atomicity (crash between saves) is not the same bug as concurrency
  (two requests at once, doc 08 F15). A single save fixes the first and does nothing for
  the second. Say so in the PR, and file F15 as its own ticket that needs a migration.

### Ticket 10: Blocking a user ends their sessions
Doc 06 ex. 12 / doc 08 F16. Small diff, and a good exercise in finding *every* path.

- **Change:** `ExchangeRefreshTokenAsync` rejects (and revokes the family of) a token whose
  user `IsBlocked`; `SetUserBlockedAsync(blocked: true)` revokes the user's refresh tokens
  in the same save, reusing `ForceLogoutAsync`'s query.
- **Accept:** refresh after block → error; the family's tokens have `RevokedAt` set; unblock
  does *not* resurrect them; client-credentials refresh (no `UserId`) unaffected.
- **Trap:** before you write the fix, list every place that issues a token
  (`CreateAuthorizationCodeAsync`, the three `Exchange*` methods, `LoginAsync`) and mark
  which ones check `IsBlocked`. The table is the PR description.

### Ticket 7: OAuth-spec error responses
Doc 08 F10. Cross-cutting; touches the error philosophy of the whole API.

- **Change:** introduce `OAuthProtocolException(error, description)` (note
  [`OAuthError`](../src/NetAuth0.Application/OAuth/OAuthContracts.cs#L32) already exists,
  unused — the author left you a breadcrumb); throw it from `OAuthService` with proper
  codes (`invalid_grant`, `invalid_client`, `unsupported_grant_type`); map it in the
  exception handler to `{"error": …, "error_description": …}` with 400/401; all *other*
  unhandled exceptions become 500.
- **Accept:** token endpoint failures are spec-shaped; non-OAuth endpoints keep
  ProblemDetails; an unexpected exception returns 500 (test with a fake throwing service).
- **Trap:** `invalid_client` with Basic auth wants a 401 + `WWW-Authenticate` header per
  RFC 6749 §5.2 — the detail that separates "read the spec" from "skimmed a blog."

### Ticket 8: MFA failures count toward lockout + TOTP step replay rejection
Doc 08 F3, both halves.

- **Accept:** N wrong TOTP codes lock the account on the same counter/policy as passwords;
  the same valid code presented twice in one window fails the second time
  (`MfaEnrollment.LastUsedTimeStep`, requires a migration — rung 1 ticket 2 paid off);
  lockout audit events distinguish password vs MFA failures.

### Ticket 9: Recovery-code login
Doc 08 F6 — complete the broken loop.

- **Accept:** login accepts `recoveryCode` as an alternative to `totpCode`; code is matched
  by hash, single-use (`UsedAt`), counted toward lockout on failure; audit
  `mfa.recovery_code_used`; UI gets a "use a recovery code" affordance on the MFA prompt.
- **Design question to answer in the PR:** does using a recovery code revoke the TOTP
  enrollment (forcing re-enrollment) or not? Industry practice varies — pick one, justify.

---

## Rung 3 — Senior builds (design before code)

For each: write a one-page design note first (doc 11's RFC template), get it "reviewed"
(rubber-duck or AI-review it adversarially), then build.

### Project A: Tenant isolation, end to end
Doc 08's F2+F8 class fix. Claims-derived tenant context, unscoped-lookup audit of every
service method, `ValidateAudience` enabled, two-tenant integration test suite proving
cross-tenant 403s. **The design note must address:** where tenant resolution lives
(middleware vs authorization handler vs service), how M2M tokens map to tenants, and the
migration path for existing admin SPA tokens.

### Project B: Real hosted authorize (kills F1)
Replace `user_id`-from-query with an authenticated session: provider-side login page (or
reuse of the SPA with a first-party cookie), consent screen honoring `RequireConsent`
([`IdentityEntities.cs:56`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L56) —
currently stored, never read: another decorative flag), and the dashboard itself converted
to consume the standard flow (closing doc 04 flow 1's "first-party bypass"). This is the
capstone — it touches every layer and retires the repo's worst finding.

### Project C: Token lifecycle operations
The doc 09 backlog as one coherent system: background cleanup service
(`IHostedService`) with per-table retention; `UserId` index on refresh tokens; security
metrics (the four golden signals) exposed via OpenTelemetry; `reuse_detected` alert hook.
**Design note must address:** cleanup vs evidence-retention tension, batch sizing so the
job never locks the tokens table during a login storm.

### Project D: Key management hardening
Encrypt PEMs at rest (ASP.NET Data Protection as the pragmatic middle before KMS), cache
signing keys with rotation-safe invalidation (the doc 09 perf item — measure before/after),
JWKS caching headers, and the rotation runbook from doc 09 turned into an admin-facing doc.

---

## Definition of Done (every rung)

- [ ] Red test existed before the fix; suite green after.
- [ ] `dotnet build` + tests + `npm run build` (when frontend touched) pass. If your test
      lives in `tests/NetAuth0.IntegrationTests`, CI does **not** run it today (doc 07,
      "Where Tests Go"); either add the CI step in a separate PR first or say plainly in
      your PR that the test is local-only.
- [ ] PR description: problem, approach, **risk + rollback**, test evidence (doc 11).
- [ ] No drive-by refactors — one ticket, one diff.
- [ ] You can answer "what breaks if this is wrong, and how would we notice?" — if the
      answer is "we wouldn't notice," the ticket isn't done (add the audit event/metric/test
      that makes failure visible).
