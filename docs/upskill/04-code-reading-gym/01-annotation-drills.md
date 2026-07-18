# Annotation Drills

Annotate with six labels: inputs, outputs, dependencies, invariants, side effects, failure modes.

## Drill 1: `OAuthService.ExchangeAuthorizationCodeAsync`

Read [`ExchangeAuthorizationCodeAsync`](../../../src/NetAuth0.Application/OAuth/OAuthService.cs#L107-L153).

| Label | What To Find |
| --- | --- |
| Inputs | `TokenRequest` fields and cancellation token. |
| Outputs | `TokenResponse` with access token, ID token, refresh token. |
| Dependencies | DB context, secret generator, token issuer, audit writer, clock. |
| Invariants | code exists, code active, redirect URI exact match, PKCE challenge matches. |
| Side effects | consumes code, creates refresh token, saves DB, writes audit event. |
| Failure modes | expired code, reused code, wrong redirect URI, wrong verifier, client secret failure. |

**Rubric:** Basic = labels branches. Solid = explains why each check exists. Strong = identifies transaction and concurrency tests.

## Drill 2: `IdentityService.LoginAsync`

Read [`LoginAsync`](../../../src/NetAuth0.Application/Identity/IdentityService.cs#L49-L95).

Focus on the difference between authentication and authorization. Password verification authenticates the user; permissions for admin endpoints come later from claims emitted by [`JwtTokenIssuer`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53).

**Checkpoint questions:**

- Which branches write audit events?
- Which branches mutate the user?
- Which branch returns `RequiresMfa = true`?
- Why is the dashboard app lookup a coupling point?

**Rubric:** Basic = follows happy path. Solid = follows failed password and MFA challenge. Strong = proposes test cases for lockout reset and MFA failure.

## Drill 3: `AdminService.CreateApplicationAsync`

Read [`CreateApplicationAsync`](../../../src/NetAuth0.Application/Admin/AdminService.cs#L28-L54).

Write down:

- Which values are generated server-side.
- Which values are trusted from the request.
- Which values need validation before production.
- Which table constraints backstop the service.

Then inspect [`AppDbContext`](../../../src/NetAuth0.Infrastructure/Persistence/AppDbContext.cs#L36-L57) for the constraints.

**Rubric:** Basic = finds generated client ID/secret. Solid = connects redirect/grant rows to DB constraints. Strong = identifies tenant-isolation and redirect-URI validation as separate concerns.

## Drill 4: React Query Mutation

Read [`ApplicationsPage`](../../../frontend/src/pages/ApplicationsPage.tsx#L21-L78).

Annotate:

- Local state: `createdSecret`.
- Server state: `apps`.
- Form state: `useForm`.
- Mutation side effect: cache invalidation.
- Failure mode: body sent from `unknown` API helper, comma-split fields, stale tenant ID.

**Rubric:** Basic = names hooks. Solid = separates local/form/server state. Strong = explains cache key isolation.

## Drill 5: JWT Claims

Read [`JwtTokenIssuer.IssueUserTokensAsync`](../../../src/NetAuth0.Infrastructure/Security/JwtTokenIssuer.cs#L17-L53).

Mark which claims identify:

- subject
- tenant
- client
- scope
- token type
- permissions
- nonce

**Rubric:** Basic = finds claims. Solid = distinguishes ID token from access token. Strong = explains what goes wrong when permissions are stale in a long-lived token.

## Drill 6: Startup Pipeline

Read [`Program`](../../../src/NetAuth0.Api/Program.cs#L21-L154).

Trace service registration separately from middleware execution. Mark each item as build-time registration or runtime request behavior.

**Rubric:** Basic = lists middleware. Solid = explains ordering. Strong = identifies schema creation fallback and HTTPS metadata settings as environment-sensitive risks.
