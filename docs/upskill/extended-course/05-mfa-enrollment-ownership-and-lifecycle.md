# 05. MFA enrollment ownership and lifecycle

Read [AuthController.cs](../../../src/NetAuth0.Api/Controllers/AuthController.cs), [IdentityService.cs](../../../src/NetAuth0.Application/Identity/IdentityService.cs), and [the entity mapping](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs). This lesson examines authorization and lifecycle boundaries using synthetic enrollment records. It does not run requests against a live identity service.

Authentication answers who made a request. Ownership answers whether that identity may act on this enrollment. Possession of a verification code is another fact, and database uniqueness is another. A complete enrollment contract needs to explain all of them rather than let one stand in for the others.

## Beginning enrollment uses the principal identity

BeginMfa is authorized and calls BeginTotpEnrollmentAsync with CurrentUserId. The helper chooses NameIdentifier first, then sub, and parses the value as a GUID. The service loads that user, generates a TOTP secret, adds an enrollment, generates ten recovery codes, stores their hashes, saves, and returns the enrollment identifier, secret, provisioning URI, and raw recovery codes.

Returning enrollment material is part of this selected setup flow. The course must not reproduce real values in logs, reports, or examples. Use synthetic labels and assert shape or identity through controlled doubles where appropriate. A real integration fixture should handle its generated secrets locally without printing them.

The selected method does not first revoke existing enrollments or check for an already pending enrollment. Repeating the begin operation can therefore add another record and another set of recovery-code hashes under the displayed source flow. Whether the database or another policy restricts that requires inspecting the mapping and actual behavior.

The mapping declares an index on user and type for enrollment, without IsUnique in the shown configuration. Login later uses SingleOrDefault over verified, unrevoked enrollments. That combination makes multiple active verified enrollments an important fixture: the query expects at most one, while this index alone does not enforce it.

## Verification has a different identity path

VerifyMfa is authorized but forwards only VerifyMfaRequest to the service. That request carries EnrollmentId and Code, not the current principal identity. VerifyTotpEnrollmentAsync loads by enrollment identifier, verifies the code against its stored secret, stamps verification time, and saves.

The selected service query does not bind the enrollment to the authenticated user. This is a source-level authorization gap to investigate with a synthetic negative-test matrix. It is not proof of an executed exploit, and knowledge of another enrollment's valid code remains a separate precondition. The correct finding names the missing binding rather than claim unrestricted access to every enrollment.

A proposed command should derive the actor identity from the principal and include it in the scoped lookup or explicit authorization check. Client-supplied user identity is not an equivalent authority. A test should use valid authentication for user A and an enrollment owned by user B so it reaches the ownership boundary rather than fail at missing authentication.

For denied verification, assert that VerifiedAt, RevokedAt, recovery-code records, and relevant accepted audit state remain unchanged under the chosen contract. A response status alone cannot show whether mutation happened before denial.

## Lifecycle questions before implementation

Define whether a user may have one pending and one active enrollment, multiple devices, or only one enrollment total. Define what happens when setup is restarted, when a pending setup expires, and when an active device is replaced. Those are product choices not established merely by the Type string totp.

If the contract allows only one active enrollment, activation should atomically preserve that invariant under concurrent verification. A preflight count can improve feedback but does not establish uniqueness. The actual storage strategy must be chosen and tested with competing contexts.

Recovery codes belong to the user's setup history, but the selected entity links them to user rather than a particular enrollment. Repeating setup creates another batch without an enrollment identifier on those rows. A replacement policy must decide whether old unused codes remain valid, are revoked, or are replaced. Do not invent a batch relationship that the current model does not contain.

The selected code shows creation and entity Use behavior, but this lesson has not established a complete recovery-code login path. Avoid promising that generated recovery codes can currently complete a login until the actual consumption service and route are identified. A returned setup field is evidence of generated material, not automatically a finished recovery feature.

## Login ticket versus durable challenge

When login requires MFA but lacks a code, the service returns a generated LoginTicket. The selected method does not persist it, associate it with an expiry, or show a consumer. A proposed challenge workflow would need those bindings and a clear policy for password proof, MFA attempts, expiration, and replay.

Do not treat the mere presence of a random-looking string as proof of a complete challenge protocol. Trace where it is stored, what authenticates its use, and when it becomes invalid. If the client instead resubmits password and TOTP to the existing login method, describe that actual interaction separately from a ticket-based proposal.

A design exercise can model a challenge with user, tenant, expiry, attempt count, consumed state, and expected authentication stage. Keep it explicitly educational until the application implements and tests it. The course should teach how to identify missing contracts without silently adding a new authentication system to the source tree.

## Ownership parity across adjacent routes

Sessions and session revocation both derive CurrentUserId in the controller and pass it into user-scoped service queries. Beginning MFA also derives the principal identity. Verification's different shape therefore stands out when the routes are compared side by side.

A route matrix should include unauthenticated caller, valid owner, valid different user, missing enrollment, revoked enrollment, already verified enrollment, invalid code, and malformed identifier. Each row should identify the first intended rejection and unchanged state. Some rows need product decisions before exact public response categories can be specified.

This comparison is more useful than a generic instruction to add authorization everywhere. It identifies the existing principal-derived pattern and the one operation that lacks equivalent ownership input. A narrow proposed repair can preserve surrounding behavior while adding a concrete missing boundary.

## Independent exercises

Exercise NA05-A: compare principal identity propagation in BeginMfa, VerifyMfa, Sessions, and RevokeSession. Design an owner-versus-other-user fixture that reaches verification ownership without exposing real secrets.

Exercise NA05-B: define one-active-enrollment behavior under two concurrent verifications and explain why the displayed nonunique index and SingleOrDefault call are not sufficient enforcement.

Exercise NA05-C: distinguish generated recovery codes, a returned login ticket, and an implemented recovery/challenge protocol. Propose the minimum durable records and acceptance tests for one selected extension.

Review [identity workflow answers](solutions-identity-workflows.md) after preparing the matrix.
