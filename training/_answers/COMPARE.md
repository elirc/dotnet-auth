# COMPARE — your review vs. the sealed answer

Fill this in after you've done `training/review/EXERCISE.md` and read
`docs2/08-security-review-walkthrough.md` + `training/_answers/review.md`.

## 1. Line up your findings against the ranked list

| Your finding | Did you find the matching rank-1..14 item? | If missed, which review pass would have caught it? |
|---|---|---|
| | | |

(Add a row per finding you wrote. "Which pass" refers to the four-pass method in
`docs2/08-security-review-walkthrough.md` §"The Method": trust-boundary, asset,
state-machine, negative-space.)

## 2. What you missed — pattern, not just list

For each miss, answer in one sentence: *why* did you miss it — not "I didn't see it," but
what habit would have surfaced it. Examples of real patterns (fill in your own, don't copy
these):

- "I read every function individually but never diffed two functions that do the same job
  (the refresh-grant secret check vs. the auth-code-grant secret check) — that's the
  state-machine/inconsistency check, and I don't do it by default."
- "I didn't grep for callers of `RecoveryCode.Use` — I assumed if a type exists and is
  populated, something reads it. That's the negative-space pass; I don't currently run it at
  all."

## 3. What you found that isn't on the sealed list

If you flagged something not in `training/_answers/review.md`, don't discard it — decide:
real finding the sealed list missed, or a false positive, and say why either way.

## 4. Severity calibration

Pick 3 items where your severity differed from the sealed rank. For each: was your call
defensible for a *different* deployment context (e.g. single-tenant vs. multi-tenant), or
was it a miscalibration? Severity is contextual — say what context you were implicitly
assuming.

## 5. One-line self-assessment

Which of the four passes (trust-boundary / asset / state-machine / negative-space) is your
weakest right now, based on this run? That's the thing to deliberately practice on the next
DRILL/LAB project.
