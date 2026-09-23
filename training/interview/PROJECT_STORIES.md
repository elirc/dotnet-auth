# Project stories — NetAuth0

Complete these yourself after doing the review + both incidents. Each is a STAR skeleton
(Situation/Task/Action/Result) tied to something real in this repo — fill in Action/Result
from what you actually did, don't invent detail you didn't do.

## 1. Debugging story — the clock-skew 401 spike

- **Situation:** `training/incidents/001-clock-skew-401-spike.md` — a hardening PR
  (`ClockSkew = TimeSpan.Zero` in `Program.cs`) shipped and caused an intermittent 401 spike
  across all tenants.
- **Task:** ______ (what were you asked to do — triage a live incident? review a PR
  post-hoc?)
- **Action:** ______ (how did you narrow "6% of requests 401, no pattern by tenant" down to
  a one-line `TokenValidationParameters` change? What did the `IDX10223` log message and the
  sub-2-second `ValidTo`/`Current time` gap tell you that a generic "401s are up" alert
  didn't?)
- **Result:** ______ (the fix, the regression test you'd add, and — this is the part
  interviewers actually probe — what you'd change about the *review process* so this class
  of bug doesn't ship again)

## 2. Trade-off story — redirect_uri exactness vs. mobile deep-link flexibility

- **Situation:** `training/incidents/002-redirect-uri-open-redirect.md` — a well-intentioned
  compatibility PR (MOBILE-114) weakened `ValidateRedirectUri` from exact match to prefix
  match, and it was reported as an open redirect / auth-code interception path.
- **Task:** ______
- **Action:** ______ (explain the trade-off you'd present to the mobile team: why exact
  match is non-negotiable in OAuth, and what alternative — fixed custom-scheme URI + `state`
  carrying the nonce — gets them what they actually needed)
- **Result:** ______

## 3. Incident story — reading a security report end to end

- **Situation:** an external report lands with a working exploit reproduction
  (`training/incidents/002-redirect-uri-open-redirect.md`'s report section) instead of a
  metrics page.
- **Task:** ______
- **Action:** ______ (how do you separate "confirmed regression" from "known limitation" —
  note the report brief explicitly calls out that the unauthenticated `user_id` parameter on
  `/connect/authorize`, `OAuthController.cs:22`, is a *different*, pre-existing issue used
  only to demonstrate the redirect bug in isolation. How do you keep a live incident from
  scope-creeping into every adjacent finding from the review?)
- **Result:** ______

## 4. Code-review story — finding the pattern behind a miss

- **Situation:** `training/review/EXERCISE.md` / `training/_answers/COMPARE.md`.
- **Task:** ______
- **Action:** ______ (pick one finding you missed in your blind review — e.g. rank 5, the
  refresh grant skipping the client-secret check when the field is omitted
  (`OAuthService.cs:183-186`) vs. the auth-code grant which requires it unconditionally
  (line 115). What review *habit* — not "look harder" — would have caught it?)
- **Result:** ______ (what you now deliberately do differently when reviewing auth code)

## 5. Agent-caught-bug story (if you ran `training/agentic/` materials on a mid/senior
   ticket from another project, or verified either incident's fix with an agent)

- **Situation:** ______
- **Task:** ______
- **Action:** ______ (what did the implementer agent get right/wrong, and what did the
  adversarial reviewer catch that you'd have missed reading the diff alone?)
- **Result:** ______
