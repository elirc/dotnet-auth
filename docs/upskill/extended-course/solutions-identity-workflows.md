# Identity workflow solutions

Complete the login and enrollment traces before reading these answers. They explain current source and proposed verification boundaries, not an executed live authentication scenario.

## NA04-A: lockout sequence

The first four wrong passwords increment failure count and do not reach the threshold. The fifth increments to five and sets lockout to that attempt's time plus fifteen minutes. Each wrong-password branch saves and writes login.failure before throwing.

A retry while lockout is in the future takes the earlier blocked branch, writes login.blocked, and does not increment through RegisterFailure. At exact expiry the strict comparison no longer blocks. Another wrong password increments the retained count and sets a new deadline because the count remains at or above five.

The service does not automatically reset count merely because time passed. Reset occurs on the successful path after MFA processing. This distinction reconciles the permissive entity method with the actual service gate order.

## NA04-B: password success is not final success

With a verified unrevoked enrollment and no code, the service returns RequiresMfa true with a generated ticket and no tokens. Wrong MFA audits failure and throws. Neither branch reaches ResetFailures. Correct MFA, or absence of such enrollment, reaches reset and save, then login.success audit.

Tokens can still be absent when the dashboard application is missing. Token issuance can also fail after the success audit. Therefore credential acceptance, failure-counter reset, audit event, and token availability are separate observations.

A component should not infer authenticated API capability solely from RequiresMfa false. It needs the returned token contract and subsequent authorization behavior. Those UI and validator paths require additional source tracing and tests.

## NA04-C: audit failure policy

When blocked-account auditing fails, the intended locked-account exception has not yet been thrown. When token issuance fails after login.success, the audit can already describe successful credential processing even though the caller receives no normal token result.

A proposed policy should define event meaning and whether audit durability is required for acceptance. It may use an atomic local record or a durable intent, but that mechanism is not established merely by sharing a DbContext. AuditWriter calls SaveChangesAsync itself, adding another persistence boundary.

Inject failures at exact points and read state through a fresh context. Assert counters, accepted audit rows, and response categories separately. Do not replace the failure matrix with one generic exception test or claim an outer transaction without evidence.

## NA05-A: ownership propagation

BeginMfa, Sessions, and RevokeSession derive CurrentUserId and pass it to their service methods. VerifyMfa forwards only enrollment identifier and code. The selected verification service loads by enrollment identifier without a user condition.

A proposed negative fixture authenticates synthetic user A and supplies user B's enrollment with a controlled valid-code double. It should reach ownership validation and leave verification state unchanged. Missing authentication or an always-invalid code would stop earlier and fail to establish the intended boundary.

This does not claim that arbitrary real enrollments can be verified without their code. It identifies missing principal binding under the selected source path. Keep the test synthetic and avoid printing enrollment secrets.

## NA05-B: one active enrollment

Define one-active behavior before choosing implementation. Under two concurrent valid verifications, either one wins and the other conflicts, or an explicit replacement policy atomically revokes the previous enrollment. Both designs require a precise accepted-state oracle.

The displayed user/type index is not unique, and SingleOrDefault is a query expectation rather than a constraint. Multiple verified unrevoked records can therefore violate that expectation under the shown model unless another uninspected boundary prevents them.

A real adapter test must establish the chosen atomic activation behavior. A preflight count or a mock returning one enrollment cannot prove it. Include repeated setup and prior recovery-code policy so the lifecycle remains coherent beyond the immediate activation call.

## NA05-C: material versus protocol

Generated recovery codes show setup material was returned and hashes were stored. A returned LoginTicket shows a random value was produced. Neither alone demonstrates a complete consumption protocol with ownership, expiry, attempts, and replay behavior.

Choose one extension. For a durable MFA challenge, define user and tenant binding, expiry, attempt budget, consumed state, and how prior password proof is represented. For recovery codes, define atomic one-use consumption and how setup replacement affects older codes. Both need synthetic negative and competing-context fixtures.

The selected source does not justify presenting either extension as installed. The correct handoff names the missing consumer or state record and its acceptance test, rather than treating a field name as evidence of a finished feature.
