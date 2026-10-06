# Incident 001 — auth failures spiking after yesterday's deploy

**Branch:** `training/incidents/001-clock-skew-401-spike` (based on `main` @ `1d70eac`)
*(Status 2026-10-06: this branch and commit `563e916` were never pushed, so the `git diff`
below fails on a fresh clone. Recreate the branch first; see the status note under
"Incident branches" in `training/README.md`.)*
**Severity:** SEV2 — intermittent auth failures across all tenants, self-resolving per user
after ~1 refresh cycle
**Status:** Open, needs root cause

## Alert

```
[PagerDuty] NetAuth0-API — error_rate.401 (5m) > 3% — FIRING
  started: ~40 min after the 14:10 UTC deploy of NetAuth0.Api
  current: 6.2% of requests to protected endpoints returning 401
  baseline (last 7d): 0.1–0.3%
```

## What support is seeing

> Multiple tickets since this afternoon: "I got logged out in the middle of filling out a
> form", "getting 'session expired' way more often than usual", one from an admin: "my
> dashboard just 401'd, I refreshed and it fixed itself." No pattern by browser, tenant, or
> client app that support can find. Started right after the deploy that went out at 14:10.

## Logs (sampled from `NetAuth0.Api`, redacted)

```
14:52:07.114 [WRN] Authorization failed. These requirements were not met:
  DenyAnonymousAuthorizationRequirement
14:52:07.114 [INF] Bearer was not authenticated. Failure message:
  IDX10223: Lifetime validation failed. The token is expired.
  ValidTo: '2026-09-23T14:52:06Z', Current time: '2026-09-23T14:52:07.089Z'.
...
14:58:41.902 [WRN] Authorization failed. These requirements were not met:
  DenyAnonymousAuthorizationRequirement
14:58:41.902 [INF] Bearer was not authenticated. Failure message:
  IDX10223: Lifetime validation failed. The token is expired.
  ValidTo: '2026-09-23T14:58:41Z', Current time: '2026-09-23T14:58:41.847Z'.
```

Every failing request in the sample has `Current time` within **~1 second** of `ValidTo`. No
requests are failing with a `ValidTo` more than a couple of seconds in the past.

## Metrics

- `error_rate.401` on protected endpoints: 0.2% → 6.2%, step change at deploy time, flat
  since (not climbing/decaying)
- No change in `error_rate.500`, `error_rate.403`, or overall request volume
- No change in `db.connections.active` or `db.query.p95`
- The spike affects `/api/v1/admin/*` and `/api/v1/auth/sessions` roughly proportionally to
  their traffic share — not concentrated on one endpoint or one client app

## Suspect change

The deploy at 14:10 UTC shipped one PR to `NetAuth0.Api`, titled **"security: tighten JWT
validation window per SEC-482"**. Diff against the previous `main`:

```
git -C <repo> diff 1d70eac 563e916 -- src/NetAuth0.Api/Program.cs
```

(Full diff available; this brief intentionally doesn't include it inline — pull it yourself
as part of the investigation, the way you'd pull the "suspect PR" in a real postmortem.)

## Your task

1. Form a hypothesis for why *this specific pattern* (failures clustered within ~1s of the
   token's `exp`, not further in the past) started at this specific deploy.
2. Confirm it against the diff and the relevant `TokenValidationParameters` behavior.
3. Write the fix, the regression test that would have caught this before it shipped, and a
   one-paragraph postmortem.

Answer key: `training/_answers/001-clock-skew-401-spike.md` (sealed — don't open until
you've written your own hypothesis down).
