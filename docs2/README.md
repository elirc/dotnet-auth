# NetAuth0 Upskill Curriculum (v2)

A complete, code-grounded path from junior to mid/senior software engineer, using this repo —
a working Auth0-style identity provider — as the training ground. Every claim in these docs
cites real code in this repository. Nothing here is a stub.

## Why v2

The original `docs/upskill` suite had a good skeleton but 34 of its 47 files were empty TODO
stubs, and its analysis missed several real defects in the code. This version:

- **Is complete.** Twelve dense documents, no placeholders.
- **Is sequenced.** Each doc builds on the previous one; finish them in order.
- **Teaches the domain, not just the stack.** OAuth2/OIDC is the highest-leverage knowledge
  in this repo. Doc 03 teaches it directly from the code.
- **Contains real bugs as exercises.** The code-reading gym (doc 06) and security review
  (doc 08) are built around genuine defects verified by reading the source — including
  several the original docs never found.
- **Has answer keys.** You can self-check instead of guessing whether you "got it."

## The Path

| # | Doc | What you get | Time |
| --- | --- | --- | --- |
| 01 | [Orientation](01-orientation.md) | System map, glossary, run instructions, reading order | half a day |
| 02 | [Architecture](02-architecture.md) | Layers, DI, data model, the decisions and their tradeoffs | 1 day |
| 03 | [OAuth from the Code](03-oauth-from-the-code.md) | OAuth2/OIDC, PKCE, JWT, key rotation — learned from this repo | 1–2 days |
| 04 | [Flow Traces](04-flow-traces.md) | Five end-to-end traces with the subtle behavior most readers miss | 1 day |
| 05 | [Pattern Catalog](05-pattern-catalog.md) | The repo's patterns, when they work, and where this repo violates its own | half a day |
| 06 | [Code Reading Gym](06-code-reading-gym.md) | Annotation drills + ten find-the-real-bug exercises with answers | 1–2 days |
| 07 | [Testing & Debugging](07-testing-and-debugging.md) | Test strategy, concrete test code for this repo, debugging playbook | 1–2 days |
| 08 | [Security Review Walkthrough](08-security-review-walkthrough.md) | How a senior audits an auth system, then the actual findings | 1 day |
| 09 | [Performance & Operations](09-performance-and-operations.md) | Query/crypto cost analysis, observability, rotation runbook | half a day |
| 10 | [Contribution Ladder](10-contribution-ladder.md) | Tickets from first PR to senior-level builds, with acceptance criteria | ongoing |
| 11 | [Senior Mindset](11-senior-mindset.md) | Code review, PR/RFC writing, tradeoff articulation, interview prep | half a day |
| 12 | [Reference](12-reference.md) | Command cheatsheet, endpoint table, risk register, level rubric | as needed |

Total: roughly two focused weeks of study, then the contribution ladder turns study into
demonstrated skill.

## How to Use This

1. **Read with the code open.** Links use `path#Lx-Ly` anchors. Don't trust the doc — click
   through and verify. Anchors can drift if the code changes; the symbol name is the real
   reference.
2. **Do the exercises before reading the answers.** The gap between "I followed that" and
   "I produced that" is exactly the gap between junior and mid-level.
3. **Write as you go.** Keep a scratch file of what surprised you. Surprise is where learning
   happens; everything else you already knew.
4. **Ship from the ladder.** Docs 01–09 are input. Doc 10 is output. Mid-level is judged on
   output.

## What the Levels Actually Mean

- **Junior:** makes the change work. Asks "how do I do X?"
- **Mid-level:** makes the change correct. Asks "is this the right pattern here? What breaks
  if I'm wrong? How do I prove it works?" Owns a feature end to end including tests, errors,
  and edge cases, without needing the design handed over.
- **Senior:** makes the change cheap to live with. Asks "what does this commit us to, who
  pays for it later, and how do we reduce the blast radius?" Finds the problems nobody filed
  a ticket for (doc 08 is a live demonstration).

The fastest way from junior to mid is to stop reading code as "what does it do" and start
reading it as "what can go wrong" — that single habit change drives most of this curriculum.

## Honesty Notes

- This repo is synthetic: no commit history, no real production traffic. Contribution
  exercises simulate maintainer practice.
- `dotnet` was not installed on the machine when these docs were written, so backend
  commands are derived from `.github/workflows/ci.yml` and the project files rather than
  locally executed. The frontend build (`npm run build`) is verified.
- Defects called out in docs 06 and 08 were verified by reading the source, not by running
  exploits. Where a finding depends on runtime behavior (e.g., model-binding details), the
  doc says so and tells you how to confirm it.
