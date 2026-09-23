# Blind Code Review — NetAuth0

**Time box: 60 minutes.** Treat this like a security-focused design review you've been
asked to do before this identity provider goes multi-tenant production. Read the code, not
the docs.

**Do not open `docs2/08-security-review-walkthrough.md` first.** That file already contains
a full staff-level findings table for this exact repo — reading it before you review is the
equivalent of reading the answer key before the exam. Do your own pass, write down your
findings, *then* read it (and `training/_answers/review.md`, which builds on it) to compare.
`training/_answers/COMPARE.md` has the template for that comparison.

## Scope

Everything under `src/` and `frontend/src/`. You may skim `tests/` to see what's already
covered (and, more usefully, what isn't). Focus areas, in priority order:

1. `src/NetAuth0.Api/Program.cs` — auth/authz pipeline wiring, CORS, rate limiting
2. `src/NetAuth0.Application/OAuth/OAuthService.cs` — authorization_code / client_credentials
   / refresh_token grants
3. `src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs` and `SecurityServices.cs` —
   token minting, signing key handling, PKCE/secret comparison
4. `src/NetAuth0.Application/Identity/IdentityService.cs` — login, lockout, MFA
5. `src/NetAuth0.Api/Controllers/AdminController.cs` + `AdminService.cs` — tenant/authz
   boundaries on the admin surface
6. `frontend/src/api/client.ts` and the pages that call it — how tokens are stored and used
   client-side

## What to produce

A findings list, one entry per line, in this shape:

```
[SEVERITY] <one-line finding> — <file:line or file:range> — why it matters in production
```

Severities: **Critical** (account takeover / cross-tenant data access / full auth bypass),
**High** (meaningfully weakens a security control, but needs another condition to exploit),
**Medium** (real but narrow, deployment-dependent, or availability-only), **Low** (best
practice, spec deviation, or hardening).

Use an OWASP ASVS chapter tag where one clearly applies (e.g. `ASVS V3` session management,
`V4` access control, `V6` cryptography, `V7` errors/logging, `V8` data protection, `V9`
communications, `V11` business logic, `V13` API). Not every finding needs one.

Also flag anything that looks like a classic **AI-generated-code smell**: a control that's
scaffolded but never wired up, a test that asserts nothing meaningful, a config value that's
read but never enforced, a comment that claims behavior the code doesn't have.

## Constraints

- No running the app required — this is a static review. `docker-compose.yml` and
  `infra/postgres` exist if you want to spin up Postgres and actually exercise a flow, but
  it's not required to complete the exercise.
- Cite real files and line ranges. "The auth looks weak" is not a finding.
- Rank by production severity assuming this becomes a real multi-tenant IdP other apps in
  this "net-fleet" (netdrop, netlinear, netshop, netintercom) depend on — that's the
  disposition this project is actually headed for per the portfolio audit.

## When you're done

1. Open `docs2/08-security-review-walkthrough.md` and `training/_answers/review.md`.
2. Fill in `training/_answers/COMPARE.md`.
3. Move on to `training/incidents/` — two of the findings you may have logged as "Low/Medium
   — deployment dependent" are exactly what actually paged on-call in those two incidents.
