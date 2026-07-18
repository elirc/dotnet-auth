# 08 — Security Review Walkthrough

Goal: learn how a senior engineer audits an auth system by watching the method applied to
this one — then internalize the findings table. The *method* transfers to every codebase
you'll ever touch; the findings double as the repo's risk register.

## The Method (memorize this, not the findings)

A security review is not "look for bugs." It's four passes, each with a different question:

1. **Trust-boundary pass** — for every endpoint: *which inputs does the server trust, and
   should it?* Identifiers of "whose data" must come from authenticated context, never the
   request. This pass finds IDOR/tenant bugs.
2. **Asset pass** — list every secret (passwords, codes, tokens, keys); for each: how is it
   stored, transmitted, logged, and destroyed? This pass finds storage and lifecycle bugs.
3. **State-machine pass** — for every multi-step protocol (login+MFA, code→token,
   rotation): *can steps be skipped, repeated, or interleaved?* This pass finds replay and
   atomicity bugs.
4. **Negative-space pass** — what does the *absence* of code mean? Missing audit events,
   unconsumed recovery codes, configuration nothing reads. The hardest pass, because you're
   grepping for what isn't there.

Sequencing matters: trust boundaries first because they require zero domain knowledge,
negative space last because it requires all of it.

## The Findings

Severity: how bad *if this shipped as a real multi-tenant IdP*. Confidence: all findings
verified by source reading; runtime-dependent details are flagged. Findings F1–F2 would be
launch blockers; F3–F8 are pre-GA; the rest are hardening.

| # | Finding | Evidence | Severity |
| --- | --- | --- | --- |
| F1 | **Authorize endpoint mints codes for arbitrary users.** `user_id` is a query parameter; no authenticated session or consent gates it. Anyone who can reach the endpoint gets a valid code for any user of that client. | [`OAuthController.cs:22`](../src/NetAuth0.Api/Controllers/OAuthController.cs#L22) | **Critical** |
| F2 | **No tenant isolation on admin APIs.** `tenantId` is trusted from query/body; the token's `tenant_id` claim is never compared. Any `applications:manage` holder can read/mutate every tenant. Compounded by `ValidateAudience=false` ([`Program.cs:60`](../src/NetAuth0.Api/Program.cs#L60)): a token minted for *any* client passes, so the permission is the only gate. | [`AdminController.cs`](../src/NetAuth0.Api/Controllers/AdminController.cs#L15-L70), [`AdminService.cs:30-33`](../src/NetAuth0.Application/Admin/AdminService.cs#L30-L33) | **Critical** (multi-tenant) |
| F3 | **TOTP is brute-forceable.** MFA failures bypass `RegisterFailure`; no per-account counter, no per-step replay rejection (`out _` discards the matched time step). Only the per-IP 20/min limiter stands between a password-holder and the 10⁶ code space. | [`IdentityService.cs:80-84`](../src/NetAuth0.Application/Identity/IdentityService.cs#L80-L84), [`SecurityServices.cs:49`](../src/NetAuth0.Infrastructure/Security/SecurityServices.cs#L49) | **High** |
| F4 | **Refresh rotation is non-atomic.** Replacement persisted before the old token's rotation is saved, with token issuance (which can itself write — key bootstrap) in between. Crash ⇒ two active tokens per family; "≤1 active per family" is enforced nowhere. | [`OAuthService.cs:209-228`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L209-L228) | **High** |
| F5 | **Confidential clients can refresh without their secret.** Secret verified only when the request volunteers one. Stolen refresh token + public client_id suffices. | [`OAuthService.cs:183-186`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L183-L186) | **High** |
| F6 | **Recovery codes are decorative.** Generated and stored; no redemption path exists. Lost authenticator = permanent lockout, while the API advertises recovery. | [`IdentityService.cs:104-108`](../src/NetAuth0.Application/Identity/IdentityService.cs#L104-L108); `RecoveryCode.Use` has no callers | **High** (availability) |
| F7 | **Signing keys live as plaintext PEM in the database.** DB read access = forge any token for any tenant, quietly. No KMS, no encryption-at-rest, no key-access auditing — and rotation itself emits no audit event. | [`IdentityEntities.cs:283-297`](../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L283-L297), [`KeyManagementService.cs`](../src/NetAuth0.Infrastructure/Security/KeyManagementService.cs) | **High** |
| F8 | **MFA enrollment IDOR.** Verification looks up by enrollment ID only; caller's identity never checked. Part of a pattern of unscoped lookups. | [`IdentityService.cs:114-124`](../src/NetAuth0.Application/Identity/IdentityService.cs#L114-L124) | **Medium** |
| F9 | **Bearer tokens in `localStorage`.** Any XSS = token theft; 15-min expiry is the only bound. Acceptable for a lab; first-party production dashboards should use HttpOnly cookies + CSRF defense. | [`client.ts:50-64`](../frontend/src/api/client.ts#L50-L64), [`LoginPage.tsx:33-36`](../frontend/src/pages/LoginPage.tsx#L33-L36) | **Medium** |
| F10 | **Error handling collapses everything to 400** and leaks exception messages in Development; OAuth errors are not RFC 6749-shaped, so client libraries can't distinguish `invalid_grant` from `invalid_client`. | [`Program.cs:113-123`](../src/NetAuth0.Api/Program.cs#L113-L123) | **Medium** |
| F11 | **User-enumeration timing oracle.** Unknown-user login skips the ~100ms BCrypt verify; uniform message, non-uniform latency. | [`IdentityService.cs:51-64`](../src/NetAuth0.Application/Identity/IdentityService.cs#L51-L64) | **Low-Medium** |
| F12 | **Rate limiter keyed on raw `RemoteIpAddress`.** Behind any proxy/LB all users share one 20/min bucket (DoS of login); without one, attacker IP rotation defeats it. No forwarded-headers config exists. | [`Program.cs:73-83`](../src/NetAuth0.Api/Program.cs#L73-L83) | **Medium** (deployment-dependent) |
| F13 | **`plain` PKCE accepted; scope never validated;** `state` not size-bounded. Spec-permitted but below current best practice (RFC 9700 says S256 only). | [`OAuthService.cs:263`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L263) | **Low** |
| F14 | **`EnsureCreated` fallback in production path** — no migration history means no schema evolution and a trap when the first real migration arrives (doc 06, drill D). | [`Program.cs:138-150`](../src/NetAuth0.Api/Program.cs#L138-L150) | **Medium** (operational) |

Pass attribution — which pass caught what: F1, F2, F8 (trust boundary); F7, F9 (asset);
F3, F4, F5, F11 (state machine); F6, F10, F13, F14 and the missing rotation audit (negative
space). If your own review in doc 06 missed a cluster, that tells you which *pass* you
under-practice — far more useful than knowing which bug you missed.

## Fixes, Ordered by Leverage

Worth studying because *ordering fixes* is the senior deliverable — anyone can list
problems.

1. **F2 + F8 (one fix):** derive tenant/user scope from claims, never requests. Concretely:
   an authorization handler (or service-level guard) that resolves `tenant_id` from
   `RequestContext.Principal` and refuses mismatches; change service lookups to compound
   keys (`Id` + owner). One pattern, applied everywhere, closes two findings and prevents
   the *class*. Also flip `ValidateAudience` on with the admin SPA's client id.
2. **F1:** the authorize endpoint must require an authenticated session (cookie or
   first-party token) and take the user from it. Biggest single change; it's doc 10's
   capstone project because it forces you to build hosted login properly.
3. **F5 + F4 (same method):** make `ValidateClientSecretIfRequiredAsync` unconditional;
   delete the early `SaveChangesAsync` so rotation commits once. Two high findings, ~5
   changed lines, fully testable (doc 07's worked examples). **Start here if you want a PR
   today.**
4. **F3 + F10:** count MFA failures toward lockout; store last-used TOTP step; introduce a
   small exception hierarchy (`OAuthProtocolException(error, description)` + a mapped
   handler) so token errors become spec-shaped and unexpected errors become 500s.
5. **F6:** implement recovery-code redemption in the login MFA branch — feature completion
   disguised as a fix.
6. **F7, F9, F12, F14:** deployment-posture items — KMS/DataProtection for keys, cookie
   session for the first-party dashboard, `ForwardedHeaders` + smarter partition key,
   committed initial migration. File them; fix when productionizing.

## What This Walkthrough Should Change About How You Read Code

- **The absence of code is a finding.** F6 (no redemption path) and the missing rotation
  audit can't be found by reading what's there — only by asking what *should* be there.
- **Inconsistency between similar paths is a goldmine.** F5 was found purely by comparing
  the refresh path's secret check to the code path's. When two functions do the same job
  differently, one of them is usually wrong.
- **Severity is contextual.** F2 is critical *for a multi-tenant product* and irrelevant
  for single-tenant labs. Stating the context that makes a finding matter is what makes a
  report credible instead of alarmist.
- **Group findings by fix, not by location.** F2+F8 share a fix; F5+F4 share a file. A
  review that ends in three PRs beats one that ends in fourteen tickets.

## Exercise

Run pass 1 (trust boundaries) on the one surface this doc deliberately skipped: the
**frontend** ([`client.ts`](../frontend/src/api/client.ts), the pages). Questions to seed
you: what does the SPA trust from `localStorage` that another tab/extension could have
written? What does [`AuditPage`/`DashboardPage`] render from `dataJson` and is it
HTML-escaped by React by default? What would a malicious `VITE_API_URL` at build time mean?
Write your findings in the F-table format above, with severity and evidence. Then ask
whether any merit a fix PR — and if so, doc 10 is waiting.
