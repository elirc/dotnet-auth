# 05 — Pattern Catalog

Goal: name the patterns this repo uses, know when each works and fails, and — the part the
original docs skipped — see **where this repo violates its own patterns**. Recognizing a
pattern is junior; knowing its failure modes is mid; noticing the violations is senior.

Format: *shape → real example → why it works → failure modes → violation in this repo*.

## 1. Thin Controller, Fat Application Service

**Shape:** controller binds transport (query/form/JSON), calls one service method, shapes
the response. Zero business logic in controllers.

**Example:** [`OAuthController.Token`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L34-L59)
is 20 lines of binding around [`OAuthService.ExchangeTokenAsync`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L71-L80).

**Why it works:** services are testable without HTTP; the HTTP contract can change without
touching auth logic; snake_case form names live in exactly one place
([`TokenForm`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L75-L93)).

**Failure modes:** "thin" rots into "anemic pass-through plus a little logic" — and that
little logic escapes testing.

**Violation here:** [`ReadClientCredentials`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L61-L72)
is real protocol logic (Basic-auth parsing per RFC 6749) living in the controller, untested,
with a latent crash: malformed base64 throws `FormatException` → opaque 400. The *pattern*
says this belongs in the service or a dedicated binder where it can be unit-tested.

## 2. Ports & Adapters (Interfaces at the Boundary)

**Shape:** Application defines small interfaces
([`SecurityAbstractions.cs`](../src/NetAuth0.Application/Abstractions/SecurityAbstractions.cs));
Infrastructure implements them; DI binds them.

**Example:** `IClock` → [`SystemClock`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L8-L11).
Every service takes `IClock` instead of calling `DateTimeOffset.UtcNow`.

**Why it works:** time, randomness, hashing, and token signing are the four things you most
need to fake in tests. Look at the signatures: `IPasswordHasher.Verify(secret, hash)` says
*what*, BCrypt says *how*.

**Failure modes:** interface-itis — wrapping things that never vary (an interface over
`string.Trim` helps no one); or leaky abstractions where the interface shape only makes
sense for one implementation.

**Violation here:** none structural, but note `ITokenIssuer.GetJwksAsync` returns `object` —
an abstraction so loose it can't be misused *or* used safely. A typed JWKS record would cost
ten lines and catch real mistakes.

## 3. Domain Methods Over Setters (DDD-lite)

**Shape:** private setters; state changes only through intention-named methods that enforce
invariants.

**Example:** [`RefreshToken`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L239-L278) —
`RotateTo`, `Revoke`, `MarkReuse` with `??=` so revocation timestamps are write-once.
[`User.RegisterFailure`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L129-L136)
couples counting to locking so they can't drift apart.

**Why it works:** grep for `MarkReuse` and you've found every place reuse can be flagged.
Invalid single-entity states are unrepresentable.

**Failure modes:** invariants that span *entities* can't live on one entity. "At most one
active token per family" is a family-level invariant — no method on `RefreshToken` can
enforce it; only the service (plus a transaction, plus maybe a partial unique index) can.

**Violation here:** exactly that family invariant, as doc 04 Flow 3 showed. Also
`Entity.Touch` exists but is called in only one place
([`AdminService.SetUserBlockedAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L95)) —
`UpdatedAt` is effectively unmaintained, a half-applied pattern.

## 4. Check-Then-Act With Constraint Backstop

**Shape:** friendly pre-check in the service; unique index guarantees correctness under
races.

**Example:** signup duplicate check
([`IdentityService.cs:37-40`](../src/NetAuth0.Application/Identity/IdentityService.cs#L37-L40))
backstopped by the `(TenantId, Email)` unique index.

**Why it works:** the 99% case gets a clean error message; the race window gets correctness
from the database, the only place that can actually serialize.

**Failure modes:** believing the pre-check *is* the guarantee, then dropping the index; or
not handling the constraint-violation exception, so the 1% case becomes a 500 (here: an
unexplained 400).

**Violation here:** the race-loss path is unhandled everywhere — `DbUpdateException`
surfaces as the generic "Request failed."

## 5. Hash-and-Forget for Bearer Secrets

**Shape:** generate high-entropy secret → return it once → persist only `SHA256(secret)` →
look up by recomputed hash.

**Example:** auth codes ([`OAuthService.cs:42-44`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L42-L44)),
refresh tokens ([`CreateRefreshToken`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L284-L298)),
recovery codes ([`IdentityService.cs:104-108`](../src/NetAuth0.Application/Identity/IdentityService.cs#L104-L108)).

**Why it works:** plain SHA-256 (not BCrypt) is correct *here* because these are 32–48 bytes
of CSPRNG output — brute force is hopeless, so no slow hash needed, and lookup-by-hash must
be fast and indexable. Passwords get BCrypt because humans pick them from a tiny space.
**Knowing why the two hash choices differ is a classic interview discriminator.**

**Failure modes:** applying it to low-entropy inputs (SHA-256 of a 6-digit code is
crackable in milliseconds); or logging the raw secret on the way out.

**Violation here:** recovery codes are `UrlSafe(10)` — 10 random *bytes*, still fine — but
TOTP secrets are necessarily stored plaintext (the server must compute codes), which is why
MFA secret storage usually deserves encryption-at-rest. Borderline — and *not* in doc 08's
findings table; adding it as an asset-pass finding is a good self-test.

## 6. Claim-Based Authorization Policies

**Shape:** issuer puts `permissions` claims in access tokens; one policy per permission
string; `[Authorize(Policy=…)]` per endpoint.

**Example:** [`Program.cs:65-71`](../src/NetAuth0.Api/Program.cs#L65-L71) +
[`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs).

**Why it works:** authorization is declarative, visible at the endpoint, statically
greppable.

**Failure modes:** the strings drift (policy says `users:manage`, seeder writes
`user:manage`, nobody notices until a 403); and claims answer "can this principal perform
this *verb*" but never "…on this *resource*" — resource/tenant checks must still happen in
services.

**Violation here:** both halves. The permission strings are quadruplicated as raw literals
(policies, seeder, and implicitly in tokens and tests) — doc 10 ticket 3 centralizes them.
And the missing resource half is exactly the tenant-isolation gap (doc 08, F2).

## 7. Server State via React Query

**Shape:** `useQuery(key, fetcher)` for reads, `useMutation` + `invalidateQueries` for
writes; query keys include every parameter the data depends on.

**Example:** [`ApplicationsPage.tsx`](../frontend/src/pages/ApplicationsPage.tsx#L25) reads
with key `['apps', tenantId]`, the create mutation invalidates the same key (line 46);
`enabled: !!tenantId` prevents fetching with no tenant.

**Why it works:** caching, dedup, loading/error states, and refetch-on-invalidate come free;
local state (`createdSecret`), form state (`useForm`), and server state stay in their lanes —
that three-way separation is the actual lesson of this page.

**Failure modes:** a key missing a dimension serves one tenant's cached data under another
(query keys are cache *contracts*); over-broad invalidation refetches the world.

**Violation here:** [`DashboardPage.tsx:18`](../frontend/src/pages/DashboardPage.tsx#L18)
changes tenant via `location.reload()` — a full page reload to bust state instead of letting
keyed queries refetch. Honest, crude, and a tell that tenant ID should live in React state
(or route), not raw `localStorage` reads at render time.

## 8. Append-Only Audit Trail

**Shape:** every security-relevant transition writes an event: who (user/client), where
(IP/UA), what (type + `jsonb` payload).

**Example:** [`AuditWriter`](../src/NetAuth0.Application/Audit/AuditWriter.cs); ~15 call
sites; event names form a taxonomy (`login.success`, `refresh_token.reuse_detected`,
`admin.user_blocked`).

**Why it works:** incidents are reconstructed from this table; replay detection is only
*useful* because `reuse_detected` is recorded and searchable
([`SearchAuditEventsAsync`](../src/NetAuth0.Application/Admin/AdminService.cs#L147-L173)).

**Failure modes:** audit coupled to the operation's transaction (operation rolls back →
audit lies, or audit fails → operation fails); free-text event names drifting.

**Violation here:** three. Key rotation writes no event; the audit save shares the request
DbContext so an audit insert *flushes any pending tracked changes with it* (subtle!); and
event names are raw strings with no central registry.

---

## Anti-Pattern Gallery (find these in the wild, not just here)

| Anti-pattern | Sighting here | Why it bites |
| --- | --- | --- |
| **Magic strings as architecture** | `"netauth0_admin_spa"` in [`IdentityService.cs:89`](../src/NetAuth0.Application/Identity/IdentityService.cs#L89) | rename the seeded client and login silently stops returning tokens (`dashboardApp is null` → null tokens, no error) |
| **Exception-as-control-flow with one exception type** | `InvalidOperationException` for *every* business failure | handlers can't map errors to statuses; OAuth spec wants `invalid_grant` vs `invalid_client` distinctions |
| **Trusting client-supplied identifiers** | `tenantId` in admin query/body; `user_id` at authorize | the canonical IDOR setup; identifiers of *whose data* must come from the token |
| **Catch-all error flattening** | exception handler returns 400 always ([`Program.cs:113-123`](../src/NetAuth0.Api/Program.cs#L113-L123)) | DB down reads as "client sent a bad request"; alerting and clients both misled |
| **Fire-and-forget feature stubs** | `LoginTicket` never validated; recovery codes never redeemable | dead fields invite false confidence ("we have MFA recovery" — you don't) |
| **Dependencies nobody calls** | `MediatR` and `FluentValidation` in [`NetAuth0.Application.csproj`](../src/NetAuth0.Application/NetAuth0.Application.csproj), `FluentValidation.AspNetCore` in [`NetAuth0.Api.csproj`](../src/NetAuth0.Api/NetAuth0.Api.csproj) — no `IMediator`, handler, or `AbstractValidator` exists anywhere in `src/` | readers assume a pipeline/validation layer exists and go looking for it; extra supply-chain surface for zero value. Delete them or use them |
| **Non-sargable predicates** | `DisplayName.ToLower().Contains(q)` ([`AdminService.cs:79`](../src/NetAuth0.Application/Admin/AdminService.cs#L79)) | LOWER() defeats any index → sequential scan as users grow (doc 09) |

## Exercise

Pick pattern 6 (claim policies). Write a one-paragraph design note for adding the missing
resource half: a policy/handler that compares the token's `tenant_id` claim to the tenant
of the resource being touched. Decide: middleware, authorization handler with resource, or
service-level guard — and defend the choice in two sentences. Then compare with doc 08's
recommended fix for F2.
