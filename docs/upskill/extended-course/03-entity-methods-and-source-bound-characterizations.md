# 03. Entity methods and source-bound characterizations

Read [IdentityEntities.cs](../../../src/NetAuth0.Domain/Identity/IdentityEntities.cs), [Entity.cs](../../../src/NetAuth0.Domain/Common/Entity.cs), and [AppDbContext.cs](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs). Then use the [domain runner](labs/domain_lab.py) to execute selected actual entity methods in a disposable project. The lesson distinguishes private setters, domain guards, service guards, and persistence constraints.

A class can expose only private setters and still allow surprising state through its public methods. Conversely, a permissive entity method may be called safely by a service that establishes the required precondition. The correct review question is where the contract is enforced and whether every relevant caller reaches it.

## Activity predicates and mutation methods

AuthorizationCode.IsActive requires no consumed timestamp and expiration strictly greater than now. At the exact expiration instant it is inactive. Consume simply assigns ConsumedAt. It does not independently check active state and a second call overwrites the earlier consumed timestamp.

The OAuth service checks activity before consuming, which supplies a local precondition in that path. The entity method alone is not a one-use enforcement mechanism. A different caller or a stale tracked snapshot could bypass the intended sequencing unless another boundary protects it.

The mapping declares a unique index on CodeHash. That prevents duplicate stored hashes under the enforced database schema; it does not by itself compare consumption state when two contexts update the same row. The selected AuthorizationCode mapping does not invoke the concurrency configuration used on Tenant, ClientApplication, and User. These are source observations, not a substitute for an actual competing-context test.

A useful characterization intentionally calls Consume twice and asserts the later timestamp. Name it as current behavior. If a future design requires rejection or preservation of the first time, change the implementation and regression expectation together under an explicit contract.

## Refresh transitions have different repetition semantics

RefreshToken.IsActive checks revocation, reuse detection, strict expiration, and absence of replacement. RotateTo sets both replacement identity and revocation time unconditionally. Repeating it can replace the earlier successor link and overwrite revocation time.

Revoke uses null-coalescing assignment and therefore preserves the first revocation time. MarkReuse also preserves the first reuse and revocation timestamps. These three methods do not share one generic idempotency behavior. A review that describes every token mutation as idempotent because one uses a null check is inaccurate.

The source-bound runner rotates a synthetic token, revokes it again, marks reuse twice, and rotates it again. It checks the exact timestamps and identities after each operation. This exposes the local method semantics without creating any real authentication material or touching a database.

A proposed strengthened entity could reject repeated rotation or retain the original successor. That design still needs an atomic database condition for competing contexts. A guard added only to a loaded object does not arbitrate with another process holding its own earlier snapshot.

## User normalization and lockout counters

The User constructor trims and lowercases email using invariant casing. DisplayName is stored as supplied. These are different normalization contracts. The runner uses surrounding spaces and mixed email case to show the distinction without relying on real user data.

RegisterFailure increments FailedAccessCount, then sets LockoutEndsAt when the count meets or exceeds the threshold. A subsequent failure beyond the threshold sets a new deadline relative to that later now. ResetFailures clears count and lockout. The entity does not itself determine whether the login service permits another attempt while locked out; that requires reading the service.

Block and Unblock toggle IsBlocked. They do not change SecurityStamp in these methods. The runner records the original stamp and checks that it remains after block and unblock. This does not establish token revocation behavior because the token issuer and validator determine whether and how the stamp is consulted.

The base Entity initializes CreatedAt from the system clock, whereas several methods accept an explicit time. A test should not claim all entity time is injected merely because lockout or rotation receives now. If deterministic creation time becomes a requirement, it needs a separate design that accounts for the base initializer.

## Configuration is not validation

ClientApplication.Configure assigns PKCE and consent flags and token lifetimes. It copies access-token lifetime into ID-token lifetime. It does not reject negative or zero values in this method. The runner characterizes that by supplying synthetic invalid bounds and reading the stored properties.

This result does not prove an HTTP administrator can submit those values. Request contracts and admin service validation may reject them before Configure is called. The next source trace must examine those boundaries. Conversely, a validated endpoint does not make the domain method safe for every future caller.

A proposed validation strategy should state whether the domain owns lifetime bounds, whether the service duplicates them for user feedback, and which error representation the API exposes. Tests at each layer should establish that layer's contract instead of reusing a single helper assertion as proof of the entire entry path.

## Recovery codes and MFA state

RecoveryCode.Use assigns UsedAt without a local one-use guard, and repeated calls overwrite the timestamp. MfaEnrollment.Verify and Revoke independently assign their respective times. Calling Verify after Revoke does not clear RevokedAt. The object can therefore contain both a later verification time and an earlier revocation time.

Those states are not necessarily exploitable through the current service. They show why callers must define active-enrollment and unused-code predicates explicitly. A method named Verify is not the same as a complete MFA authorization workflow, and a field named UsedAt is not an atomic consumption protocol.

The teaching fixtures use obvious synthetic placeholder strings rather than functional secrets. No generated signing key, password, token, or TOTP secret is printed. The goal is to inspect state transitions and evidence, not to exercise real authentication against a running deployment.

## Running the isolated source lab

From the course directory:

```powershell
python labs/domain_lab.py
```

The runner copies the actual two domain files into a uniquely named temporary directory, builds a package-free net10 console project with package sources cleared, executes twenty-four assertions, compares original hashes, and removes only its guarded temporary directory. It does not modify the application's net8 configuration or its pinned SDK file.

The available SDK was observed as 10.0.302. This is a current environment measurement for this lab, distinct from the starter's historical unavailable-SDK record. The copied-source compilation establishes selected runtime behavior under this available target. It does not prove the original application restores or builds under its intended environment.

The runner does not execute OAuthService, EF, a real database, cryptographic signing, or HTTP validation. Those limits should appear next to the result, not disappear when summarizing a successful test count. Source identity and scope are as important as the green outcome.

## Independent exercises

Exercise NA03-A: compare Consume, RotateTo, Revoke, MarkReuse, and RecoveryCode.Use under repeated calls. Classify which overwrite, which preserve first evidence, and which reject locally.

Exercise NA03-B: trace normalization, lockout extension, and SecurityStamp across the User methods. Identify the additional service or token evidence required before claiming login blocking or access-token revocation.

Exercise NA03-C: design a disposable mutant for strict expiration and another for first-revocation preservation. State the expected failing assertion and the separate real-adapter test needed for one-use consumption.

Review [foundation answers](solutions-foundation.md) after your predictions. No mutations were executed as part of the baseline lab; mutation exercises remain proposed until independently run.
