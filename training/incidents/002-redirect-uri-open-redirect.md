# Incident 002 — external report: OAuth authorization codes can be redirected off-domain

**Branch:** `training/incidents/002-redirect-uri-open-redirect` (based on `main` @ `1d70eac`)
*(Status 2026-10-06: this branch and commit `73a0e08` were never pushed, so the `git diff`
below fails on a fresh clone. Recreate the branch first; see the status note under
"Incident branches" in `training/README.md`.)*
**Severity:** SEV1 — candidate for full account takeover, external report, not yet confirmed
exploited in the wild
**Status:** Open, needs triage and root cause before any public statement

## Report (via the security contact inbox, lightly cleaned up)

> Subject: Open redirect on /connect/authorize allows OAuth code exfiltration
>
> I registered a test OAuth client on your dev tenant with redirect_uri
> `https://app.example.com/callback`. I then sent a victim (my own second account, logged
> into the SPA) a link to:
>
> `GET /connect/authorize?client_id=<my_client>&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcallback.attacker-controlled.example%2Fx&response_type=code&scope=openid&state=...&code_challenge=...&code_challenge_method=S256&user_id=<victim_id>`
>
> The server 302'd the browser to my attacker-controlled.example host with a valid `code` in
> the query string. I was then able to complete `/connect/token` against my own client and
> receive tokens.
>
> I have not tried this against production. Reporting before doing anything further — you
> may want to confirm whether this is `redirect_uri` validation or something upstream of it,
> and whether it's new.

(Note: the reporter used a `user_id` they already controlled to demonstrate the redirect
issue in isolation. Whether `/connect/authorize` trusting a raw `user_id` query parameter is
*also* a separate, standing problem is a fair question — but it is out of scope for this
incident. This incident is specifically about whether the `redirect_uri` the server accepted
should have been rejected. Don't let the two findings blur together in your writeup.)

## What you know going in

- The client the reporter registered has exactly one redirect URI configured:
  `https://app.example.com/callback` (confirmed against the admin API / seed data).
- The redirect the server actually issued went to a URL that is **not** that exact string.
- This is a regression report, not a "here's how OAuth security review always goes" report —
  something about how `redirect_uri` is checked changed recently enough that this is worth
  bisecting.

## Suspect change

One PR touched `redirect_uri` handling recently, titled **"feat(oauth): allow redirect_uri
suffix flexibility for mobile deep links"** (ticket MOBILE-114). Diff against the previous
`main`:

```
git -C <repo> diff 1d70eac 73a0e08 -- src/NetAuth0.Application/OAuth/OAuthService.cs
```

Pull the diff yourself as part of triage, the way you would for a real bisect.

## Your task

1. Reproduce the reporter's reasoning against the diff: what comparison changed, and why
   does a string that isn't the registered URI now pass it?
2. State the actual attacker capability this grants (not just "it's insecure" — what can an
   attacker who controls a domain that happens to extend the registered URI's text actually
   do end-to-end?).
3. Write the fix. Consider: is "revert to exact match" sufficient, or does the mobile
   deep-link use case MOBILE-114 was solving need a *different*, still-safe mechanism? (You
   don't have to build that mechanism — say what it would need to guarantee.)
4. Write the regression test that would have caught this before it shipped.

Answer key: `training/_answers/002-redirect-uri-open-redirect.md` (sealed).
