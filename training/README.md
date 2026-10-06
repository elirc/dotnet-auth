# NetAuth0 — B01 apprenticeship materials (DRILL tier)

This repo already has an excellent standalone curriculum: `docs/upskill/` (v1, 47 files, 34
stub) and `docs2/` (v2, 12 dense files, no stubs — see `docs2/README.md`). Read those for
architecture/OIDC/pattern background if you don't already know this codebase; nothing here
duplicates them. This folder is the practice layer the batch process adds on top: a blind
security review and two realistic on-call incidents, both graded against sealed answers.

## Suggested order (~4-5 hours total)

1. **`review/EXERCISE.md`** (60 min timeboxed) — blind security-focused code review of the
   whole repo. Write your findings before opening anything else.
2. **`_answers/review.md`** + **`_answers/COMPARE.md`** (30-45 min) — compare against the
   sealed, ASVS-tagged, severity-ranked findings (built on `docs2/08`'s existing findings
   table, re-ranked and re-tagged for this batch). Fill in the comparison template honestly.
3. **`incidents/001-clock-skew-401-spike.md`** (45-60 min) — on-call-style incident, no hint
   of cause. Form a hypothesis from the alert/logs/metrics, pull the suspect diff yourself,
   then check `_answers/001-clock-skew-401-spike.md`.
4. **`incidents/002-redirect-uri-open-redirect.md`** (45-60 min) — external security report
   this time, not a metrics alert. Same process; check
   `_answers/002-redirect-uri-open-redirect.md`.
5. **`interview/PROJECT_STORIES.md`** (30 min) — turn what you just did into 5 STAR stories.
   Do this right after the incidents while the diagnostic path is fresh.
6. **`interview/TECHNICAL_QUESTIONS.md`** (as needed) — 20 questions across OAuth/OIDC, token
   validation, refresh-token concurrency, ASVS-flavored security, multi-tenancy, and
   operability. The DRILL tier only requires 10; all 20 are here so you can pick by target
   role. No sealed answers — the review + incidents already gave you the evidence trail.

## What's in `_answers/` (sealed — don't open before attempting the matching exercise)

- `review.md` — 14 ranked findings, ASVS chapter tags, AI-generated-code-smell flags
- `COMPARE.md` — template for scoring your own blind review against the sealed one
- `001-clock-skew-401-spike.md` — root cause, diagnostic path, fix, regression test,
  postmortem for incident 1
- `002-redirect-uri-open-redirect.md` — same, for incident 2

## Incident branches

> **Status (checked 2026-10-06): these branches do not exist on the remote.**
> `git branch -r` on a fresh clone of `github.com/elirc/dotnet-auth` shows only
> `origin/main`. Commits `563e916` and `73a0e08` are not in the cloned history either
> (`git cat-file -t 563e916` fails). The
> `git diff 1d70eac 563e916` / `git diff 1d70eac 73a0e08` commands in the incident briefs
> therefore **cannot run from a fresh clone**.
>
> Workaround: recreate each suspect diff yourself on a scratch branch from `main`, using the
> one-line descriptions below. Reading them spoils part of the "find the suspect change"
> step, but the root-cause analysis, fix, regression test, and postmortem are still the
> whole exercise:
>
> ```
> git switch -c training/incidents/001-clock-skew-401-spike main
> # Program.cs: inside TokenValidationParameters (lines 56-62) add  ClockSkew = TimeSpan.Zero,
> git switch -c training/incidents/002-redirect-uri-open-redirect main
> # OAuthService.cs ValidateRedirectUri (line 250): string.Equals(x.Uri, redirectUri, Ordinal)
> #   -> redirectUri.StartsWith(x.Uri, StringComparison.Ordinal)
> ```
>
> The sealed answer keys in `_answers/` show those same one-line changes, so they remain
> usable. `1d70eac` is still in `main`'s history (it's the initial commit), and nothing
> under `src/`, `tests/` or `frontend/` has changed since it, so branching from current
> `main` gives the same starting code.

- `training/incidents/001-clock-skew-401-spike` (commit `563e916`, based on `main`
  `1d70eac`) — `Program.cs`: `ClockSkew = TimeSpan.Zero` added to JWT bearer validation.
- `training/incidents/002-redirect-uri-open-redirect` (commit `73a0e08`, based on `main`
  `1d70eac`) — `OAuthService.cs`: `ValidateRedirectUri` changed from `string.Equals` to
  `string.StartsWith`.

Both are small, realistic single-file diffs framed as plausible PRs (a security-hardening
ticket and a mobile-compat ticket respectively). Neither touched `main` — check them out from
their branch if you want to run/build them directly:
`git -C <repo> checkout training/incidents/001-clock-skew-401-spike -- .` (or just `git show
<branch>:<path>`; don't switch the main working tree's branch if it has other uncommitted
work).

**Build status: UNVERIFIED for both.** This machine has only the .NET 10 SDK installed;
`global.json` pins SDK `8.0.0` and `dotnet build` fails at SDK resolution before reaching the
compiler. Both diffs are small and type-safe on inspection (a config property addition and a
one-method-call swap with no signature change), but neither was confirmed by an actual build.
Verify locally if you have a matching SDK.

## Not included at this tier (DRILL, not LAB)

No `ladder/`, `navigation/`, or `agentic/` — those are LAB-tier phases. No
`SYSTEM_DESIGN.md`/`MOCK_DEFENSE.md`/`FLASHCARDS.md` — DRILL's interview kit is
`PROJECT_STORIES.md` + `TECHNICAL_QUESTIONS.md` only, per the batch brief.
