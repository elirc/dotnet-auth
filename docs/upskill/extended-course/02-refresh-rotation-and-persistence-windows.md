# 02. Refresh rotation and persistence windows

Continue in [OAuthService](../../../src/NetAuth0.Application/OAuth/OAuthService.cs). Focus on ExchangeRefreshTokenAsync, CreateRefreshToken, RevokeFamilyForReuseAsync, and RevokeRefreshTokenFamilyAsync. This lesson is a source-level failure analysis. It does not assert that a database race or exploit was executed.

Refresh rotation is often summarized as replace the old token. That phrase hides at least three identities: the presented token, its replacement, and the family linking generations. It also hides two persistence calls in this implementation. A useful trace must retain all three identities and both saves.

## Required fields and conditional client authentication

The selected method first requires refresh token and client identifier. It resolves a client allowed to use the RefreshToken grant. It calls the client-secret validator only when the request supplies a nonblank secret. Therefore a configured secret hash alone does not cause this particular call site to invoke validation when the request omits the secret.

The validator itself rejects a missing or wrong secret when a stored hash exists. The important issue is reachability: a strong helper cannot enforce a rule when the caller skips it. This is a concrete source observation about the selected path, not a general statement about every client type or a live authentication test.

A proposed policy should explicitly classify clients and determine when client authentication is required. Then a service test can cover missing, blank, wrong, and correct secrets for each supported class. Do not fix the exercise by assuming that an optional request property is always harmless or always forbidden. First state the intended contract, then compare the implementation.

## Lookup and expiration order

The method hashes the presented refresh token and looks it up under the resolved client application. If absent, it rejects. It checks ExpiresAt before checking IsActive. An expired token is revoked, saved, and reported expired. An inactive token that is not caught by the expiration branch invokes family-reuse handling.

That order matters for evidence. Presenting an expired previously rotated token follows the expiration branch before the reuse branch. A learner who predicts family-wide reuse marking for every inactive token has ignored guard precedence. Design fixtures that isolate expiry and inactive state instead of relying on one token satisfying both conditions.

The service uses the injected clock at several points. Tests should provide a fixed or deliberately advancing clock and state which behavior is intended. A clock that crosses expiration between checks can reveal assumptions, but a deterministic fixture is easier to interpret than depending on wall time.

The loaded user is found by identity when the refresh token has a user identity. The displayed refresh path does not repeat the issuance path's IsBlocked check. This is another source-bound observation requiring a policy decision and targeted test before making a product claim about immediate user blocking or revocation.

## The first save creates a persistence window

CreateRefreshToken generates a new secret, hashes it for storage, carries the family identity forward, records the predecessor identity, and sets an expiration from application policy. The replacement entity is added and SaveChangesAsync is called before existing.RotateTo is invoked.

After that first save, the replacement may be persisted while the predecessor has not yet been marked rotated by this method. The service then rotates the predecessor in memory, issues user or client tokens, saves again, audits, and returns the replacement secret in the response.

The precise outcome under failure depends on surrounding transaction and context behavior. The method shown does not create an explicit encompassing transaction. Reading the context and calling environment is necessary before claiming more. At minimum, the code exposes a two-save sequence that should be represented explicitly in tests rather than treated as one atomic operation by a mock.

Consider token issuance throwing after the first save. Consider the second save failing. Consider auditing failing after the second save. These are different schedules with different potential durable state. One generic throws test that asserts no response cannot distinguish them.

## Family identity and reuse response

A refresh family links successive generations. On inactive-token reuse, the service loads every token with the same family identifier, marks each as reused using the clock, saves, and writes an audit event. The query in this helper uses family identity rather than adding the client and tenant conditions present in earlier lookup.

Whether family identity is globally unique by construction and constraint is a separate storage question. A design review should inspect that evidence rather than either assume isolation or declare a cross-tenant problem from the missing predicate alone. The course's discipline is to trace the actual key and its enforced uniqueness.

Explicit user revocation queries by user and family, revokes each returned token, saves, and audits when a tenant can be derived from the returned family. An empty result still reaches SaveChangesAsync but does not produce that audit event. A session list, meanwhile, selects root refresh tokens with no predecessor and joins application names. A family shown through its root is not the same as a list of every currently active generation.

This distinction affects UI wording. A root token can have an old expiration or revocation timestamp while later family members carry different state. Before describing a session as active or expired, define whether the UI is reporting the root record, family aggregate, or current generation. The selected query's fields alone do not establish a family-wide status calculation.

## Competing refresh requests

Construct two contexts that both read the same active predecessor. Each can generate a different replacement before seeing the other's changes. A local IsActive result does not arbitrate between them. The database may reject one later through a mapped concurrency token, or the workflow may permit more state than intended; inspect and execute the actual adapter contract to decide.

A proposed atomic rotation should define one accepted successor, predecessor consumption, family evidence, and returned outcome under lost response. It must also decide how legitimate near-simultaneous client requests differ from suspicious replay, if the product supports that distinction. Those policy choices should not be invented silently inside a retry loop.

Blindly retrying a failed transaction can repeat token generation and audit work. A safe implementation needs a known transaction boundary and a clear strategy for recovering the original accepted outcome. This lesson does not add such a protocol; it prepares a testable specification and identifies where the current source needs stronger evidence.

## Independent exercises

Exercise NA02-A: produce a guard-order table for missing secret, wrong secret, expired token, inactive unexpired token, and blocked user. State current source behavior without assuming a desired authentication policy.

Exercise NA02-B: draw both SaveChangesAsync calls and inject failure between them, during token issuance, and after the second save during audit. Identify the reads needed to determine durable state in each fixture.

Exercise NA02-C: design a two-context rotation test and a session-status interpretation test. Explain why a helper mock or a root-token list cannot independently prove one active successor per family.

Review [foundation answers](solutions-foundation.md) after writing the schedules. Use synthetic identifiers and never place token values into teaching logs.
