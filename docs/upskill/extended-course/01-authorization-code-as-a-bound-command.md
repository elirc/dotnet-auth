# 01. Authorization code as a bound command

Read the current [OAuthService](../../../src/NetAuth0.Application/OAuth/OAuthService.cs), [security interfaces](../../../src/NetAuth0.Application/Abstractions/SecurityAbstractions.cs), and [security helper tests](../../../tests/NetAuth0.UnitTests/SecurityTests.cs). This lesson traces the selected implementation. It does not claim protocol certification, a running identity server, or an executed integration suite.

The existing starter records a historical documentation pass and its environment limitations. Those records remain untouched. This extension uses current file content as its evidence and separates source observations from executed checks. A past statement that an SDK was unavailable is not a measurement of the current machine, and an old test count is not a result from this course.

## Why several identities are necessary

An authorization request involves a client identifier, an authenticated user identifier supplied to the service, the application's tenant, a registered redirect URI, a requested scope string, a nonce, a state value, and a proof-key challenge. These values answer different questions. Treating them as one login token makes it difficult to explain which value must match at exchange and which is returned to the browser.

The service first requires response_type exactly code using ordinal comparison. It then resolves the client and requires AuthorizationCode among its allowed grants. Client lookup includes redirect URI and grant collections. A known client with the wrong grant is rejected before any authorization code is created.

Redirect validation compares the supplied string against registered strings with ordinal equality. A host prefix, visually similar URL, different path, or altered case does not become accepted merely because it looks related. The lesson's claim is about the implemented comparison, not about every rule a registration endpoint should enforce. Registration validation is a separate source boundary.

The service resolves the user by both user identity and the application's tenant and rejects a blocked user. This binding prevents the selected creation path from simply accepting any existing user identifier with any application. The authentication and authorization that supplied userId are outside this method and must be traced in the controller before claiming the complete entry path is protected.

## Proof-key checks at issuance and exchange

At creation, ValidatePkce requires a nonempty challenge when the application requires PKCE and checks the method against S256 or plain under the same condition. This means the application's RequirePkce setting changes which issuance guards run. It is inaccurate to describe the method as unconditionally requiring S256.

At exchange, the service requires nonempty code, redirect URI, client identifier, and verifier before resolving the application. It derives the expected challenge using the stored method: S256 hashes the verifier, plain uses it directly, and any other method throws. It then compares expected and stored challenge through the secret abstraction's FixedTimeEquals.

These are two different stages. A creation path that accepts some input because a configuration flag disables a guard does not guarantee that the later exchange can process the stored method. A useful fixture starts with each configuration and method combination and follows it all the way through the selected code, rather than stopping after successful issuance.

The existing unit test exercises stable hashing and equality for one synthetic verifier and a wrong value. It does not invoke OAuthService, validate a redirect, issue an authorization code, or execute the controller. The distinction is not criticism of the small test; it is the foundation for designing the next test at the correct boundary.

## Persisting the code and returning a redirect

The service generates a random URL-safe code and stores its hash together with tenant, client application, user, redirect URI, scope, nonce, challenge, method, and a five-minute expiration derived from the injected clock. The returned raw code and stored hash have different roles. The raw value is needed by the client; the stored representation supports lookup without storing that same raw value in this entity.

After adding the entity, the service saves and then writes an audit event. It constructs a redirect only after the audit call returns. The separator is selected according to whether the registered redirect string already contains a question mark. Code and state are URI-escaped before appending.

The sequence creates a failure question: if the code save succeeds and the audit operation fails, the service has not returned a normal redirect even though a code may be persisted. This lesson does not claim an observed incident or prescribe immediate repair. It asks the learner to distinguish the persistence point from the success response and identify the additional audit implementation and transaction evidence needed.

Avoid logging the generated code to make the exercise easier. Synthetic labels such as code-A and its expected hash are sufficient for reasoning. A diagnostic packet should record stable non-secret correlation and outcome categories, not authentication material. The teaching task is to understand bindings and side effects, not to collect real credentials.

## Exchange binds the original decision

The exchange path resolves the client and validates its secret when required by the application's stored secret hash. It hashes the supplied code and searches by both hash and client application identity, including the user navigation. A code belonging to another client does not satisfy that selected query even if the raw value is otherwise valid.

The entity must be active at the injected current time. The request redirect must equal the redirect stored with the code, again using ordinal comparison. This second comparison binds exchange to the issuance decision; it is not merely another check against the application's current registration list.

After proof-key comparison succeeds, the code is consumed in memory, user tokens are issued, a refresh token entity is added, and SaveChangesAsync persists the tracked changes. Audit follows. The returned response includes access token, token type, lifetime, optional ID token, refresh secret, and stored scope.

A complete one-use guarantee needs persistence concurrency evidence. Two contexts could potentially read an active code before either saves; whether one later fails depends on entity mapping, constraints, and transaction behavior. The active check and Consume call show local intent. They should not be promoted to a proven cross-process guarantee without inspecting and testing the actual storage contract.

## A useful negative-test matrix

Separate wrong grant, unknown client, unregistered redirect, wrong tenant user, blocked user, expired code, consumed code, wrong exchange redirect, unsupported stored challenge method, and incorrect verifier. Each fixture should reach its intended guard. Supplying an unknown client in every row does not test redirect or proof-key behavior because execution stops earlier.

For rejection after a code is loaded, compare code consumption fields and refresh-token rows before and after. A correct error category alone does not prove no effect occurred. For issuance rejection, establish that no code entity was persisted and no accepted issuance audit was recorded under the chosen contract.

Some expected outcomes require product decisions. For example, how should a response describe an unavailable or invalid code without exposing unnecessary detail? The current implementation throws exceptions with specific messages; HTTP mapping and public error policy require another source read. Keep the service-level result separate from the eventual HTTP status and body.

## Independent exercises

Exercise NA01-A: draw the binding graph for tenant, client, user, code, redirect, and challenge. Give one invalid fixture for each edge and identify which current query or comparison examines it.

Exercise NA01-B: compare RequirePkce true and false with S256, plain, and an unsupported method. Distinguish creation acceptance from exchange acceptance and explain why the existing helper test is insufficient evidence for the whole matrix.

Exercise NA01-C: build a failure trace where code persistence succeeds but auditing fails. State what can be concluded from source, what remains unknown, and which test would establish the complete transaction behavior without exposing raw credentials.

Use [foundation answers](solutions-foundation.md) only after completing your own trace. No application changes are required for this lesson.
