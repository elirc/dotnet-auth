# 04. Login guard order and audit meaning

Read [IdentityService.cs](../../../src/NetAuth0.Application/Identity/IdentityService.cs), [IdentityContracts.cs](../../../src/NetAuth0.Application/Identity/IdentityContracts.cs), and [AuditWriter.cs](../../../src/NetAuth0.Application/Audit/AuditWriter.cs). This lesson follows current login and signup control flow. It does not claim that the HTTP endpoint, password implementation, or database has been exercised.

A login outcome is the result of ordered gates. Tenant lookup, user lookup, blocking, lockout, password verification, MFA, state reset, auditing, application lookup, and token issuance each have different effects. The order determines what gets persisted and which audit label is justified if a later step fails.

## Signup normalization and uniqueness

Signup resolves an active tenant by slug, normalizes email with trim and invariant lowercase, checks whether a user already has that email in the tenant, hashes the password, adds a user, saves, and audits. The entity constructor normalizes email again, so the selected service and domain agree on that representation.

The preflight AnyAsync check improves the ordinary error path but is not an atomic uniqueness guarantee. Two requests can both see absence before either saves. The mapping's unique tenant/email index is the storage-level evidence to inspect, and a real adapter test must establish how a collision is translated to the public contract.

Do not test uniqueness with two different tenants unless that is the intended case. The selected index permits the same normalized email in different tenants. A useful matrix includes same tenant with email case differences, different tenant with the same email, and concurrent same-tenant creation. Those cases exercise different identities.

The request record shown contains plain string properties without displayed validation attributes. That does not prove there is no global validation elsewhere, but it means this record alone does not establish password length, email syntax, or display-name bounds. Trace registration and pipeline validation before claiming endpoint behavior.

## Login rejects before password work

Login first resolves an active tenant and then a user under that tenant with normalized email. Missing tenant and missing user both produce Invalid credentials in the selected service. It then checks IsBlocked or a lockout end strictly later than the clock.

A blocked or currently locked user causes a login.blocked audit before the service throws Account is temporarily locked. Password verification is not reached. At the exact lockout end, the greater-than comparison no longer blocks on that timestamp. The next password attempt can proceed, subject to other conditions.

The audit call is awaited before the intended rejection is thrown. If the audit writer fails, the observed exception can differ from the ordinary locked-account result. This does not make the original rejection invalid; it shows that diagnostic persistence is part of the control flow and needs its own failure policy.

A fixture intended to test bad-password counting must use an active tenant, an existing unblocked user, and no active lockout. Otherwise it stops at an earlier gate and cannot prove the counter behavior. Name the reached gate in each test rather than treating every rejected login as equivalent.

## Password failures and threshold behavior

A wrong password calls RegisterFailure with a threshold of five and a fifteen-minute lockout window, saves, audits login.failure with reason bad_password, and throws Invalid credentials. The domain counter behavior from chapter three now has a concrete service policy.

The fifth failure sets a lockout deadline. A following request while that deadline is still in the future takes the earlier blocked branch and does not call RegisterFailure through this path. The entity's ability to extend lockout on additional direct failure calls therefore does not mean every repeated HTTP attempt extends it. Service reachability changes the result.

After the deadline passes, another wrong password reaches RegisterFailure again with the existing count unless another operation reset it. Since the count is already at or above the threshold, it creates a new fifteen-minute deadline. This source trace is more precise than saying the counter automatically resets when lockout expires; no such reset appears before the wrong-password branch.

A successful password does not immediately clear failures. MFA processing happens first. Missing or incorrect MFA can exit before ResetFailures. A test should capture count and lockout values through each branch so that a changed MFA flow does not silently alter password-failure semantics.

## MFA branches and successful-login evidence

The service finds one verified, unrevoked enrollment for the user. If one exists and no TOTP code is supplied, it generates a login ticket, audits a challenge, and returns a response requiring MFA with no access or ID token. The displayed method does not persist that ticket or show a later ticket-consumption operation.

When a code is supplied, the selected TOTP service verifies it. A failed code audits mfa.failure and throws, without using RegisterFailure in this branch. A proposed MFA attempt limit must therefore be designed and tested explicitly rather than inferred from the password counter.

After MFA succeeds or is not required, the service resets password failures, saves, writes login.success, then looks up the dashboard application and asks the token issuer for tokens if it exists. This order gives the audit label a limited meaning: login.success can be recorded before later token issuance fails.

If the dashboard application is absent, tokens are null and the service still returns RequiresMfa false with user information. A UI contract should distinguish successful credential verification from obtaining credentials that authorize subsequent API calls. The boolean alone is not evidence that an access token exists.

## Audit is another SaveChanges boundary

AuditWriter serializes optional data, adds an AuditEvent to the same abstract context dependency, and calls SaveChangesAsync. The writer is not merely an in-memory log statement. Its save can persist tracked changes present in the context as well as the added event, depending on how the context is shared and used.

The selected methods often save their business changes before invoking audit. This creates separate persistence boundaries unless an outer transaction groups them. Do not assume a shared context means all SaveChanges calls across the method are one atomic commit.

An audit design should define which events describe attempted, accepted, or completed work. A token-issuance failure after login.success may require a separate event or more precise naming. The right answer is not to delete audit because it complicates the trace; it is to align evidence labels with the actual point they are written.

Use synthetic correlation and bounded fields in fixtures. Signup's audit data includes normalized email, so a privacy review should consider what audit readers need and how records are retained. The teaching runner should not emit real personal data merely to demonstrate serialization.

## Independent exercises

Exercise NA04-A: trace five wrong passwords, one blocked retry, an expired lockout, and another wrong password. Give counter, deadline, reached branch, and audit type at each step.

Exercise NA04-B: compare missing MFA, wrong MFA, correct MFA, and no enrollment after a correct password. State when failures reset and when tokens may still be absent.

Exercise NA04-C: inject audit failure before locked-account rejection and token-issuer failure after login.success. Explain the evidence labels and propose a testable policy without claiming an outer transaction exists.

Use [identity workflow answers](solutions-identity-workflows.md) after making your predictions.
