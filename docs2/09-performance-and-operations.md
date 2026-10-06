# 09 — Performance & Operations

Goal: think about cost and runtime behavior the way a senior does — *before* there's an
incident, by reading code — and know how you'd operate this system. Performance thinking is
mostly two skills: knowing the **unit costs** (a BCrypt verify, an RSA import, a sequential
scan) and spotting **multipliers** (per-request, per-token, per-row).

## Unit Costs in This Codebase

Rough orders of magnitude — calibrate them once with a benchmark, then carry them in your
head:

| Operation | Where | Cost | Multiplier |
| --- | --- | --- | --- |
| BCrypt verify, work factor 12 | every login ([`SecurityServices.cs:15-16`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L15-L16)) | ~100–250 ms CPU | per login attempt — **deliberately** slow; this is the design |
| RSA-2048 keygen | first token after rotation/bootstrap ([`JwtTokenIssuer.cs:124`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L124)) | tens–hundreds of ms | rare — fine |
| RSA PEM import + sign | **every JWT** ([`CreateJwtAsync`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L94-L110)) | ~1–5 ms | ×2 per user login (access + ID token), each with its own DB key fetch and PEM import |
| SHA-256 of a token | every code/refresh lookup | microseconds | ignore |
| Indexed point query (`TokenHash`, `ClientId`) | token exchanges | sub-ms | healthy |
| Permissions join (UserRoles→Role→Permissions) | every user-token issuance ([`JwtTokenIssuer.cs:19-23`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L19-L23)) | one 3-join query | per token pair |

The two findings worth acting on eventually, neither urgent:

1. **Token issuance does redundant work per call** — fetch newest key row, parse PEM,
   rebuild `RsaSecurityKey`, twice per login (and JWKS rebuilds *all* keys per request).
   The fix shape is a small cache keyed on `KeyId` invalidated on rotation — but measure
   first; at lab scale this is noise. The senior point is noticing that the *clean* fix
   needs care: a cached `RSA` instance must not be disposed per request, and rotation must
   bust the cache across instances.
2. **Login's serialized waterfall** — tenant query → user query → BCrypt → MFA query →
   dashboard-app query → permissions query → 2 signs → saves + audit saves. Eight-plus
   round trips. Fine at lab scale; the first thing to consolidate if login latency ever
   matters (and the BCrypt verify dominates anyway, which is correct).

## The Queries That Will Age Badly

Reading LINQ for its SQL translation is a mid-level superpower. Three to study:

- **User search:** `x.Email.Contains(q) || x.DisplayName.ToLower().Contains(q)`
  ([`AdminService.cs:79`](../src/NetAuth0.Application/Admin/AdminService.cs#L79)) →
  `LIKE '%q%'` plus `LOWER(...)` — neither can use a B-tree index ⇒ sequential scan per
  keystroke-ish search. At 10⁴ users nobody notices; at 10⁶ the admin page times out.
  Fix ladder: prefix-match (`StartsWith`, sargable) → `pg_trgm` GIN index → real search.
  Note `Email` is already lowercase by construction, so the `ToLower` on it would be pure
  waste — the author knew that; the DisplayName one is the honest cost.
- **Audit search:** `EventType.Contains(eventType)`
  ([`AdminService.cs:152`](../src/NetAuth0.Application/Admin/AdminService.cs#L152)) — same
  substring problem on a table that **only grows**, plus the count query in
  [`PagedResult`](../src/NetAuth0.Application/Common/PagedResult.cs#L9) does the scan
  *twice* per page view. The `(TenantId, CreatedAt)` index saves the sort; the filter
  doesn't use it. Audit tables eventually need partitioning or retention — say it in the
  design review *before* the table hits 100M rows.
- **Sessions list:** fine today (indexed by user via FK? — actually no index on `UserId`!
  check [`AppDbContext.cs:101-106`](../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L101-L106):
  only `TokenHash` unique and `FamilyId` are indexed). `GetSessionsAsync` filters on
  `UserId` ⇒ sequential scan of refresh tokens. Small now; unbounded growth since rotated
  tokens are never purged. **Two compounding omissions: no index and no cleanup job.**

That last point generalizes: `AuthorizationCodes` (consumed, 5-min lifetime) and
`RefreshTokens` (revoked/expired) have no deletion path anywhere in the repo. Every code
exchange adds a permanent code row and a refresh-token row, every refresh adds another
refresh-token row, and every dashboard login adds audit rows. (Dashboard login itself issues
no refresh token, see doc 04 flow 1.) The operational fix is a periodic cleanup job — which doubles
as doc 10's background-services ticket.

## N+1s and Their Absence

Worth noticing what the repo does *right*: list endpoints project to DTOs inside the query
([`ListApplicationsAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L56-L70)
shapes redirect URIs and grants in one round trip), pagination clamps `pageSize ≤ 100`
([`PagedResult.cs:8`](../src/NetAuth0.Application/Common/PagedResult.cs#L8)), and the
permissions query uses `SelectMany` + `Distinct` server-side. When you review EF code, the
questions are always: *does the query shape match the DTO shape* (projection), *is there a
hidden per-row query* (N+1), and *is anything unbounded* (missing Take/clamp).

## Observability: What Exists, What's Missing

Exists: Serilog console logs + request logging ([`Program.cs:16-19,112`](../src/NetAuth0.Api/Program.cs#L16-L19)),
`/healthz` with a PostgreSQL probe ([`Program.cs:85`](../src/NetAuth0.Api/Program.cs#L85)),
and — underrated — the **audit table as a security-metrics source**.

Missing, in the order you'd add it:

1. **Metrics on the four golden security signals:** login failure rate, lockout rate,
   `reuse_detected` count, token issuance rate. All derivable from audit events today with
   SQL; a real deployment exports counters (OpenTelemetry + Prometheus).
2. **Correlation IDs** propagated into audit events, so "this reuse_detected" links to
   "this request log line."
3. **Alerting thresholds:** any `reuse_detected` (page-worthy — it's a theft signal);
   login-failure spike (credential stuffing); JWKS 4xx/5xx from resource servers
   (rotation gone wrong).

## Runbook: Zero-Downtime Signing-Key Rotation

The operational reasoning every IdP operator must be able to reproduce. Constraints: access
tokens live 15 min ([`IdentityEntities.cs:57`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L57));
JWKS serves keys retired <7 days ([`JwtTokenIssuer.cs:71`](../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L71));
verifiers cache JWKS for some TTL (JwtBearer default ~hours, refreshes on unknown `kid`).

```
1. Pre-check    JWKS endpoint healthy; note current kid(s).
2. Rotate       POST /api/v1/admin/signing-keys/rotate  → new kid.
3. Verify issue decode a fresh token; header kid == new kid.
4. Verify old   old-kid token still validates (old key still in JWKS).
5. Watch        401/validation-failure rate on resource servers for max(token
                lifetime, JWKS cache TTL). Spike ⇒ a verifier pinned keys or
                can't reach JWKS.
6. Retire fully old key row may be deleted only after: token lifetime AND all
                caches expired AND no refresh path re-issues with it. The 7-day
                window makes "never rush this" the default. 
Rollback:       there isn't one — you can't un-publish a key safely. Forward-fix
                only: rotate again. (This asymmetry is why rotation needs an
                audit event — F7/doc 10 ticket 1.)
```

Compromise variant (key leaked): same flow but step 6 happens **immediately**, accepting
that all outstanding tokens die — availability sacrificed for integrity, and the 7-day
retired-key window in `GetJwksAsync` becomes the thing you *override*, not respect. Knowing
which knob to break in an emergency is what the runbook is actually for.

## Exercise

1. Write the SQL (or LINQ) for the security dashboard's four golden signals from the
   `AuditEvents` table, per tenant, per hour.
2. The cleanup job: specify retention rules for `AuthorizationCodes`, `RefreshTokens`, and
   `AuditEvents` — and justify why the three answers are different. *(Hint: codes are
   worthless after 5 min + consumption; dead refresh tokens are evidence in a reuse
   investigation — keep ~90 days; audit is the investigation record — keep longest,
   partition, archive.)*
3. Estimate: at 1,000 code-flow logins/day, how many rows/year does `RefreshTokens` gain
   for a client that never refreshes vs. an SPA that refreshes every 15 minutes for an
   8-hour workday? *(Never refreshes: one row per login, ~365k/year. Refreshes: 1 + 32
   rotations = 33 rows per login, ~12M/year, roughly 33× more. Rotation multiplies rows,
   which is why the cleanup job and the missing UserId index will matter sooner than
   intuition says. The first-party dashboard contributes **zero** here: its login path
   issues no refresh token.)*
