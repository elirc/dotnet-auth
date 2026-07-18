# 06 — Code Reading Gym

Goal: build the habit that most reliably moves you from junior to mid: reading code for
**what can go wrong**, not what it does. Two parts: annotation drills (warm-up), then ten
find-the-real-bug exercises. Every bug in part two is genuinely present in this codebase —
verified by source reading, not invented for the exercise.

Rules: work on paper or a scratch file. No peeking at the answer until you've committed to
one in writing. Grade yourself honestly.

---

## Part 1 — Annotation Drills

Annotate each target with six labels: **inputs, outputs, dependencies, invariants, side
effects, failure modes**. The point of the labels: a function's bugs almost always live in
its *side effects* and *failure modes*, the two things its signature doesn't show.

**Drill A:** [`ExchangeAuthorizationCodeAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L107-L153).
Target: find all 7 failure modes and all 4 side effects (consume, refresh insert, save,
audit). Bonus: which side effect can happen *inside* `IssueUserTokensAsync` that nobody
expects? *(Key bootstrap can write a SigningKey — token issuance is not read-only.)*

**Drill B:** [`LoginAsync`](../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95).
Draw the branch tree. There are 6 distinct outcomes (invalid creds ×2 flavors, locked,
MFA challenge, MFA failure, success). Mark which branches save state and which only audit.

**Drill C:** [`PagedResult.CreateAsync`](../src/NetAuth0.Application/Common/PagedResult.cs#L5-L12).
Small enough to fully specify. What are the exact bounds of `page` and `pageSize` after
normalization? Why does it take `materialize`/`count` as delegates instead of calling
`ToListAsync` itself? *(Application can't reference EF's async extension methods — the
delegates invert that dependency. This is ports-and-adapters in miniature.)*

**Drill D:** [`Program.cs:138-150`](../src/NetAuth0.Api/Program.cs#L138-L150) (startup
schema block). Annotate what happens on: fresh DB + no migrations; existing DB + first
migration added later. *(Second case is the trap: `EnsureCreated`-built schemas have no
migration history, so `MigrateAsync` tries to re-create existing tables and fails.)*

---

## Part 2 — Find the Real Bug

For each exercise: read the cited code, write down (1) the defect, (2) severity
(critical/high/medium/low), (3) a one-line fix direction, (4) the test that would have
caught it. Then open the answer.

### Exercise 1 — the ticket to nowhere
Read [`IdentityService.LoginAsync` lines 72-84](../src/NetAuth0.Application/Identity/IdentityService.cs#L72-L84)
and [`LoginResponse`](../src/NetAuth0.Application/Identity/IdentityContracts.cs#L5).
Something in the MFA challenge flow is fake. What?

<details><summary>Answer</summary>

The `loginTicket` (`secrets.UrlSafe(24)`) is generated and returned but **never persisted
and never checked anywhere**. The "complete MFA" step is just re-POSTing tenant+email+
password+totpCode. Consequences: the password must be sent twice (and held by the client
between steps), and the ticket field misleads readers into thinking a challenge-session
exists. Severity: **medium** (design integrity / misleading API, not directly exploitable).
Fix: persist a short-lived hashed ticket and accept `ticket+totpCode` for completion, or
delete the field. Test: complete-MFA request with a *garbage* ticket succeeds today — assert
it shouldn't.
</details>

### Exercise 2 — brute force the second factor
Same method, [lines 58-84](../src/NetAuth0.Application/Identity/IdentityService.cs#L58-L84),
plus [`User.RegisterFailure`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L129-L136).
The lockout protects against password guessing. What doesn't it protect?

<details><summary>Answer</summary>

Wrong TOTP codes throw without calling `RegisterFailure` — **MFA attempts never count
toward lockout**. An attacker with the password gets unlimited TOTP guesses, bounded only
by the 20/min/IP rate limit (trivially spread across IPs). A 6-digit space with a ±1-step
verification window is realistically attackable. Severity: **high**. Fix: count MFA
failures (same counter or a separate one) and lock. Test: 5 bad TOTP submissions → 6th
attempt returns "locked" — fails today.
</details>

### Exercise 3 — verify whose enrollment?
Read [`VerifyTotpEnrollmentAsync`](../src/NetAuth0.Application/Identity/IdentityService.cs#L114-L124)
and its caller [`AuthController.VerifyMfa`](../src/NetAuth0.Api/Controllers/AuthController.cs#L36-L42).
Who is allowed to verify enrollment `X`?

<details><summary>Answer</summary>

Anyone authenticated. The lookup is by `EnrollmentId` alone — the current user is never
compared to `enrollment.UserId`. This is an **IDOR**: with a leaked/guessed enrollment ID,
user B can activate user A's pending TOTP enrollment. Impact is contained (B doesn't learn
the secret from this endpoint — but B *needs* the secret to produce a valid code... unless B
created the enrollment *for* A's account via another gap). The deeper rot: a pattern of
unscoped lookups. Compare `BeginTotpEnrollmentAsync`, which takes `userId` from the token.
Severity: **medium** here, **high** as a habit. Fix: `SingleAsync(x => x.Id == id &&
x.UserId == currentUserId)`. Test: user B verifying A's enrollment expects 4xx — passes
(200/204) today.
</details>

### Exercise 4 — break glass, find brick wall
Read [`BeginTotpEnrollmentAsync`](../src/NetAuth0.Application/Identity/IdentityService.cs#L97-L112),
then search the whole solution for where recovery codes are *consumed*. Take your time.

<details><summary>Answer</summary>

They never are. Ten recovery codes are generated, hashed, stored, returned — and **no login
or MFA path accepts one** ([`RecoveryCode.Use`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L354)
has zero callers). A user who loses their authenticator is locked out forever, while the
product *appears* to have recovery. Severity: **high** (availability + false safety claim).
Fix: accept a recovery code in the login MFA branch, mark `UsedAt`, revoke remaining codes
on enrollment reset. Test: login with a valid recovery code — fails today. This is the
sharpest lesson in the gym: **a feature isn't its data model; it's the complete loop.**
</details>

### Exercise 5 — the optional secret
Read [`ExchangeRefreshTokenAsync` lines 182-186](../src/NetAuth0.Application/OAuth/OAuthService.cs#L182-L186)
versus [`ExchangeAuthorizationCodeAsync` line 115](../src/NetAuth0.Application/OAuth/OAuthService.cs#L114-L115).
Same validator, different calling convention. Spot the hole.

<details><summary>Answer</summary>

The refresh path validates the client secret **only if the request includes one**
(`if (!string.IsNullOrWhiteSpace(request.ClientSecret))`). Omit the secret entirely and a
confidential client's stolen refresh token is redeemable with just the public `client_id`.
The code path validates *honest* clients and waves through *malicious* ones — a textbook
inverted check. RFC 6749 §6 requires confidential clients to authenticate at refresh.
Severity: **high**. Fix: call `ValidateClientSecretIfRequiredAsync` unconditionally (it
already no-ops for public clients — that's the *right* conditionality, on the *app*, not
the request). Test: confidential client refresh without secret → expect `invalid_client` —
succeeds today.
</details>

### Exercise 6 — everything is your fault
Read the exception handler ([`Program.cs:113-123`](../src/NetAuth0.Api/Program.cs#L113-L123)).
PostgreSQL goes down. What status code do API consumers see, and why is that worse than it
sounds?

<details><summary>Answer</summary>

**400 Bad Request** — for a database outage. The handler hardcodes 400 for every unhandled
exception. Consequences cascade: client retry logic won't retry (4xx = "don't retry");
error-rate dashboards show a client-error spike instead of a server incident; OAuth clients
get non-spec errors. Severity: **medium-high** (operational). Fix: map known business
exceptions to 400 (or better, typed errors), everything else to 500/problem+json; emit
`error`/`error_description` per RFC 6749 §5.2 on token endpoints. Test: throw from a probe
endpoint, assert 500.
</details>

### Exercise 7 — the zombie session list
Read [`GetSessionsAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L82-L89) and
recall rotation (doc 04, flow 3). A user logs in, the app refreshes its token twice, then
the user opens "sessions." What does the UI show for that session's status?

<details><summary>Answer</summary>

The query returns only family **roots** (`RotatedFromId == null`) and projects the *root's*
`ExpiresAt`/`RevokedAt`. But rotation set the root's `RevokedAt` ([`RotateTo`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L267-L271))!
The session is alive (the grandchild token is active) yet displays as **revoked**, with
stale expiry. Severity: **medium** (correctness of a security UI — users can't tell which
sessions are really alive). Fix: aggregate per family — status = "active iff any member
active", expiry = max over the family — or track session state on a separate entity. Test:
rotate twice, list sessions, assert active. Lesson: **a projection can be syntactically
fine and semantically wrong; "what does each column mean after every state transition?" is
a review question, not a nitpick.**
</details>

### Exercise 8 — two tokens, one family
Read [`ExchangeRefreshTokenAsync` lines 209-228](../src/NetAuth0.Application/OAuth/OAuthService.cs#L209-L228)
with transaction-shaped eyes. The process dies at line 217 (token signing). Database state?

<details><summary>Answer</summary>

Replacement token: **persisted, active** (saved at 211). Old token: **still active** —
`RotateTo` ran only in memory; the save that would persist it (227) never happened. Two
active tokens in one family; the caller received neither. The client retries with the old
token: it works (still active), minting a *third* family member. The orphan replacement
floats, redeemable by anyone who... nobody — its secret died with the response that never
sent. So: not directly exploitable, but the family invariant is broken and the audit trail
under-counts. Severity: **medium**. Fix: wrap the whole rotation in
`db.Database.BeginTransactionAsync` (or one single `SaveChangesAsync` at the end — both
mutations are in one context!). Notice the simplest fix is *removing* the early save.
Test: fault-injection between saves (throwing fake `ITokenIssuer`), assert old token still
sole-active.
</details>

### Exercise 9 — the unreachable configuration
Read [`ValidatePkce`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L256-L267),
[`OAuthController.Authorize`'s parameters](../src/NetAuth0.Api/Controllers/OAuthController.cs#L13-L23),
and [`ExchangeAuthorizationCodeAsync` line 109](../src/NetAuth0.Application/OAuth/OAuthService.cs#L109).
A client has `RequirePkce = false`. Can it actually complete a code flow without PKCE?

<details><summary>Answer</summary>

No. Three independent gates force PKCE regardless of the flag: (1) `code_challenge` binds
as a required non-nullable string at the controller — `[ApiController]` rejects its absence
with 400 *(runtime model-binding behavior — confirm with a request once the API runs)*;
(2) the exchange demands `code_verifier` unconditionally; (3) with an empty stored
challenge, no verifier hashes to `""`. The `RequirePkce=false` configuration is **dead** —
a setting that silently doesn't do what it says. Severity: **low** (PKCE-always is the
*safer* behavior!) but the lesson is high-value: **configuration that doesn't configure is
a bug even when the forced behavior is good**, because the next engineer trusts the flag.
Fix: either honor the flag end-to-end or delete it and document PKCE-required.
</details>

### Exercise 10 — second factor, second use
Read [`TotpService.Verify`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L46-L50).
The `out _` is the bug. Why?

<details><summary>Answer</summary>

`VerifyTotp(code, out long timeStepMatched, …)` reports *which* time step matched precisely
so callers can store it and **reject reuse of the same step** — RFC 6238 §5.2 requires a
verified code be accepted only once. Discarding it means a shoulder-surfed or phished TOTP
code is replayable for the rest of its ~90-second window (±1 step). Severity: **medium**.
Fix: persist `LastUsedTimeStep` on `MfaEnrollment`; require `matched > LastUsedTimeStep`.
Test: same code accepted twice today — assert second verify fails. Lesson: **an ignored
out-parameter from a security library is a flashing light — the API author put it there
for a reason.**
</details>

---

## Scoring

| Found unaided | Level signal |
| --- | --- |
| 0–2 | expected junior — redo doc 04 and return in a week |
| 3–5 | solid junior → mid transition underway |
| 6–8 | mid-level reading skills; you're review-ready |
| 9–10 | senior-grade adversarial reading |

The repeatable technique, if you want to name it: for every function ask **"who calls
this, with what they shouldn't; what does it skip when inputs are absent; what's true
in memory but not yet on disk; and which field does it return that nothing enforces."**
Those four questions found all ten bugs.
