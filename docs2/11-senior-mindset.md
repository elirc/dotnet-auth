# 11 — Senior Mindset

Goal: the non-code half of mid/senior — reviewing, writing, and articulating tradeoffs.
Everything here uses this repo's material so you can practice immediately, but the skills
are the portable ones.

## Code Review: What Each Level Sees

Take a concrete PR: *"adds a new admin endpoint `POST /api/v1/admin/tenants/{id}/suspend`
with `[Authorize]`."* Three reviews of the same diff:

- **Junior review:** style, naming, "looks good, works locally." Approves.
- **Mid review:** `[Authorize]` alone is wrong here — every other admin action requires a
  permission policy ([`AdminController`](../src/NetAuth0.Api/Controllers/AdminController.cs)
  sets the local convention). Where's the audit event? Where's the test? What's the
  response code contract?
- **Senior review:** all of the above, plus: suspending a tenant interacts with *every*
  flow — do existing tokens for that tenant's users keep working until expiry (15 min of
  zombie access)? Should it revoke refresh-token families tenant-wide? Is `{id}` from the
  route yet another instance of the F2 trust-boundary problem? And: is "suspend" reversible
  — what's the unsuspend story before we ship the one-way door?

The pattern: **juniors review the diff, mids review the diff against the codebase's
conventions, seniors review the diff against the system's invariants.** The five cross-flow
invariants at the end of doc 04 are exactly the checklist the senior is running.

A practical review protocol you can adopt today:

1. Read the PR description first; if you can't tell what breaks when it's wrong, ask
   before reading code.
2. Find the tests; read them *before* the implementation — they tell you what the author
   thinks the contract is.
3. Diff-level pass (correctness, conventions), then one **invariant pass**: which of the
   system's standing guarantees does this touch?
4. Write comments as questions when uncertain, as statements with reasons when certain,
   and always distinguish *blocking* from *take-it-or-leave-it*. "Non-blocking: …" is free
   to type and saves a round trip.

## Writing PRs That Get Approved Fast

The template, with a real example from doc 10 ticket 6:

```
## Problem
Refresh rotation performs two SaveChangesAsync calls with token issuance between
them (OAuthService.cs:209-228). A crash mid-rotation leaves two active tokens in
one family, breaking the "≤1 active per family" invariant that replay detection
assumes. (docs2/08, F4)

## Approach
Removed the early save; the child insert and parent RotateTo now commit in a
single SaveChangesAsync. Considered an explicit transaction instead — rejected
because both writes share one DbContext, so a single save is equivalent and
simpler; written up in the PR thread.

## Risk & rollback
Behavior-identical on the happy path. If the single-save reordering surfaces an
FK timing issue, revert is one commit; no schema change.

## Test evidence
- New: Rotation_crash_between_insert_and_rotate_leaves_old_token_active (red
  before fix — output attached)
- Existing replay-family test: green
- Full suite: 14 passed
```

Why this works: the reviewer can evaluate the *decision* (single save vs transaction)
without reconstructing it, the risk section answers the question they'd otherwise have to
ask, and the red-test-first evidence is proof of correctness, not assertion of it. Note
what's absent: no narration of the diff ("then I changed line 211…") — the diff narrates
itself.

## RFCs / Design Notes

For anything rung-3 sized (doc 10), write one page *before* coding. The skeleton that earns
senior credibility:

1. **Problem & non-goals** — one paragraph each. Non-goals are where most scope arguments
   are won before they start ("this RFC does not address M2M tenant mapping").
2. **Constraints** — what's fixed: spec requirements (RFC 6749 §6), existing invariants
   (doc 04's five), compatibility (discovery document is a published contract).
3. **Options, honestly costed** — two or three, with the *prices* table from doc 02 as the
   model. The tell of a fake RFC is a strawman option; the tell of a real one is that the
   rejected option is genuinely tempting.
4. **Recommendation + switch signal** — what you'd do *and what future evidence would
   change the answer*. ("Direct context is right until the same tenant-scoped query
   appears in three services" — doc 02.) The switch signal is the senior move: it converts
   a decision into a monitored decision.
5. **Rollout & rollback** — including the doc 09 lesson that some changes (publishing a
   signing key, shipping a discovery document change) have *no* rollback, only forward
   fixes — those need staged rollout instead.

Practice prompt: write this one-pager for doc 10 Project A (tenant isolation). Time-box to
45 minutes. The hard part is honest costing of "authorization handler" vs "service-level
guard" — if your options table makes one of them look obviously dumb, you haven't steel-
manned it.

## Articulating Tradeoffs (interviews and design reviews)

The compressed repertoire from this repo — each is a 60-second answer you should be able
to deliver with code citations:

| Prompt | The shaped answer |
| --- | --- |
| "JWT vs sessions?" | Stateless verification vs revocation latency. This repo: 15-min JWTs + revocable refresh families splits the difference — revocation truth lives only at the refresh boundary ([`OAuthService.cs:199-203`](../src/NetAuth0.Application/OAuth/OAuthService.cs#L199-L203)). The dashboard-in-localStorage choice is the *same* tradeoff decided the risky way (doc 08 F9). |
| "Why hash refresh tokens with SHA-256 but passwords with BCrypt?" | Entropy. 48 CSPRNG bytes can't be brute-forced, so fast+indexable wins; human passwords need deliberate slowness (doc 05, pattern 5). One hash decision per threat model, not one hash policy per company. |
| "Why rotate refresh tokens at all?" | Theft *detection*, not just prevention: rotation makes the legitimate client and the thief collide — the family revocation turns an invisible compromise into an audit event (doc 03, stage 4). |
| "Repository pattern?" | "It depends" is junior; "not yet, and here's the switch signal" is senior (doc 02's table). |
| "How would you make this multi-tenant-safe?" | Don't enumerate bugs — name the *class* (identifiers of whose-data must come from the token, never the request) and the one pattern that fixes it everywhere (doc 08, fix 1). |

The meta-skill in all five: **name the axis, place both options on it, then place *this
system* on it with evidence.** Definitions don't get you hired at mid+; placement does.

## Behavioral Stories From This Repo

You'll be asked "tell me about a time you found a serious bug / improved reliability /
disagreed about design." If your real history is thin, the work you do in docs 06–10 *is*
legitimate material — synthetic codebase, real engineering. Structure each as
situation→action→result with one number:

- *Found:* "Auditing an auth service, I noticed the refresh path validated the client
  secret only when one was supplied — comparing it against the sibling code path that
  validated unconditionally. Stolen tokens were redeemable without client auth. Fixed in
  ~5 lines with a red-test-first PR." (doc 06 ex. 5 → doc 10 ticket 5)
- *Designed:* the tenant-isolation RFC — especially if you can talk about the option you
  *rejected* and why.
- *Disagreed:* doc 06 ex. 9 is a ready-made one: "the safe behavior was already forced, so
  I argued for deleting the configuration flag rather than implementing it — dead config
  that promises behavior it doesn't deliver is a maintenance trap."

## Maintainer Communication (for when you take this to real OSS)

The norms that make maintainers take you seriously, in one list: open an issue before a
non-trivial PR and propose the approach; keep PRs single-purpose (doc 10's
definition-of-done bans drive-bys for this reason); when reviewers push back, treat their
codebase knowledge as data you don't have; when *you* find a security issue in a real
project, use the private disclosure channel (SECURITY.md), never a public issue — doc 08's
findings table format is exactly what a good private report looks like.

## Self-Assessment

You're operating at mid level when, without prompting, your PRs include risk sections,
your reviews catch convention violations, and you write the red test first. You're
operating at senior level when you catch *invariant* violations in review, your design
notes include switch signals, and — the realest tell — **you find and file the F-table
problems nobody asked you to look for.** Doc 08's exercise (frontend trust-boundary pass)
is the standing test: when your own pass produces findings with correct severity and
honest "this doesn't matter because…" entries, you're there.
