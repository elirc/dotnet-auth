# NetAuth0 Training Lab

> **Status (2026-10-06):** 36 of this suite's 47 core files are still one-line
> `TODO: not yet filled` stubs, so most of the table below leads to empty pages. The complete,
> code-verified curriculum for this repo is [`docs2/`](../../docs2/README.md), 12 chapters
> from orientation to the security review. Start there. The [`extended-course/`](extended-course/README.md)
> here is filled in and can serve as a deeper follow-on, though it was not re-verified in the
> 2026-10-06 pass. The test suite is one `[Fact]`
> in `tests/NetAuth0.UnitTests/SecurityTests.cs`.

This suite turns NetAuth0 into a training lab for a junior engineer moving toward mid-level and senior work. NetAuth0 is a synthetic identity-provider project: a .NET 8-style API layered into API, Application, Domain, and Infrastructure projects, plus a React/Vite admin dashboard. It implements OAuth/OIDC surfaces, hosted login, application registration, JWT issuance, refresh-token rotation, MFA enrollment, RBAC, audit logging, and local infrastructure scaffolding. The API composition lives in [`Program`](../../src/NetAuth0.Api/Program.cs#L14-L154), the identity domain model lives in [`IdentityEntities`](../../src/NetAuth0.Domain/Identity/IdentityEntities.cs#L19-L355), and the React API boundary lives in [`api`](../../frontend/src/api/client.ts#L81-L100). Provenance is synthetic: there is no commit history and the full tree is untracked, so contribution modules are simulated maintainer practice rather than real OSS archaeology. Backend execution is not locally verified because `dotnet` is missing; frontend build and npm audit are verified.

Use this lab in four modes:

| Track | Start Here | Goal |
| --- | --- | --- |
| Brand-new junior | [`00-fast-track.md`](00-fast-track.md) | Run what can be run, name the layers, trace login and client registration. |
| Junior with .NET/React familiarity | [`01-codebase-cartography/05-key-flows.md`](01-codebase-cartography/05-key-flows.md) | Follow data and authorization across UI, API, application service, EF, and token code. |
| Mid-level new to repo | [`03-architecture-and-patterns/05-pattern-catalog.md`](03-architecture-and-patterns/05-pattern-catalog.md) | Learn the repo patterns and design small cross-layer changes without widening blast radius. |
| Senior doing architecture review | [`03-architecture-and-patterns/07-comparative-implementations.md`](03-architecture-and-patterns/07-comparative-implementations.md), then [`08-reference/risk-register.md`](08-reference/risk-register.md) | Judge tradeoffs, identify risks, and propose staged improvements. |

Conventions:

- Anchors use the form `Symbol -> relative/path#Lx-Ly` in Markdown links and only point to files opened during this session.
- If a command is called verified, the real command and trimmed output are recorded in [`verification-log.md`](08-reference/verification-log.md).
- Invented examples are labeled `// Illustrative: not from this repo`.
- Drills are meant to be done on paper or in a scratch branch.
- The senior mindset line: junior asks "how do I make it work?", mid-level asks "is this the right pattern?", senior asks "what does this commit us to, who pays, and how do we reduce risk?"

The strongest path through the suite is: fast track, key flows, pattern catalog, comparison cards, annotation drills, debugging scenarios, good-first tickets, interview prep, then the risk register.


## Extended current-source course

Continue with the [extended course](extended-course/README.md), which adds source-grounded lessons, independent predictions, and separate solutions. Its README records the current installment and evidence boundaries.
