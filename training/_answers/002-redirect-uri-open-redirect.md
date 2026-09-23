# Sealed answer — Incident 002 (redirect_uri open redirect)

**BUILD: UNVERIFIED** — same environment gap as incident 001 (no matching .NET 8 SDK
installed; only .NET 10 is present and `global.json` pins 8.0.0). The change swaps one
method call (`string.Equals` → `string.StartsWith`) with no signature or type changes, so a
compile failure is unlikely, but this was not confirmed by an actual build.
`dotnet build src/NetAuth0.Application/NetAuth0.Application.csproj` on branch
`training/incidents/002-redirect-uri-open-redirect` if you have a matching SDK.

## Root cause

`OAuthService.cs`, `ValidateRedirectUri`, diff `1d70eac..73a0e08`:

```diff
 private static void ValidateRedirectUri(ClientApplication app, string redirectUri)
 {
-    if (!app.RedirectUris.Any(x => string.Equals(x.Uri, redirectUri, StringComparison.Ordinal)))
+    // MOBILE-114: mobile deep links append a one-time nonce segment after the
+    // registered callback path, so allow any registered URI as a prefix.
+    if (!app.RedirectUris.Any(x => redirectUri.StartsWith(x.Uri, StringComparison.Ordinal)))
     {
         throw new InvalidOperationException("redirect_uri is not registered.");
     }
 }
```

This function runs only inside `CreateAuthorizationCodeAsync` (`OAuthService.cs:33`), at
`/connect/authorize` time — it's the *only* redirect_uri check on that path.
`ExchangeAuthorizationCodeAsync` still does an exact-match comparison of the code's stored
`RedirectUri` against the token request's `redirect_uri` (`OAuthService.cs:128`, unchanged),
but that only matters if the token exchange is performed by the legitimate client — it does
nothing to stop the *authorize* redirect itself from going to an attacker's URL.

`string.StartsWith` is a plain character-prefix test, with no notion of URL structure
(scheme/host/path boundaries). Given the registered URI `https://app.example.com/callback`,
the string `https://app.example.com/callback.attacker-controlled.example/x` starts with it
character-for-character — the `.attacker-controlled.example` suffix is just more characters
appended after `callback`, not a separate path segment the comparison distinguishes from a
different host. The browser has no trouble resolving that string as a URL pointing to a
completely different domain; `ValidateRedirectUri` never re-parses it as a URI to check that.

End-to-end capability: an attacker registers (or already has) any OAuth client on the
tenant, since redirect URIs are per-client and the attacker's *own* client registration
matters here — but critically, the vulnerable check runs against `app.RedirectUris`
regardless of which client is presenting the request, so an attacker doesn't even need to
own the target client if they can get `/connect/authorize` to redirect there another way.
In the reported case: attacker owns a client with `redirect_uri`
`https://app.example.com/callback`, and sends a victim a link where `redirect_uri` is a
domain that has that string as a literal prefix. If the victim already has a session (or the
standing "authorize trusts `user_id` from the query string" gap from the review is combined
with it), the server issues a real authorization `code` and 302s the victim's browser
straight to the attacker's host with `code` and `state` in the URL. The attacker's server
harvests `code` from its own access logs, then completes `/connect/token` as the same
attacker-controlled client (which does still need to pass PKCE, but the attacker generated
their own `code_verifier`/`code_challenge` pair, so that's not a barrier) and receives real
access/ID/refresh tokens for the victim.

## Fix

Revert to exact string match (`string.Equals(x.Uri, redirectUri, StringComparison.Ordinal)`)
— per RFC 6749 §3.1.2 and RFC 9700 (OAuth Security BCP), redirect URI comparison must be
exact, not prefix or substring. If MOBILE-114's actual need (dynamic per-install segments)
is real, the safe mechanism is to register a *fixed* app-specific custom-scheme URI
(`com.netauth0.app://callback`) with no dynamic suffix at all, and pass the "one-time nonce"
the mobile team wanted via `state` (which is already integrity-bound by the client and
already round-tripped) instead of encoding it into the redirect URI. That gets deep-link
flexibility without weakening the one comparison OAuth depends on being exact.

## Regression test

Add to `tests/NetAuth0.UnitTests` (or an integration test hitting `/connect/authorize`):
1. Register a client with redirect URI `https://app.example.com/callback`.
2. Call the authorize flow with `redirect_uri =
   https://app.example.com/callback.attacker.example/x` and assert it throws /
   returns an error — **not** a 302.
3. Also assert the exact registered URI still succeeds (prevents a fix that's too strict,
   e.g. accidentally requiring trailing-slash-exact-match when the registration didn't have
   one).
4. Keep both as permanent tests — this is the single highest-value regression test in the
   whole OAuth surface, since redirect_uri exactness is what stops authorization-code
   interception, and "make redirect_uri matching more lenient" is a plausible, well-intentioned
   PR that could regress it again.

## Postmortem (model)

*Impact:* a regression on `/connect/authorize`'s redirect_uri validation allowed authorization
codes to be delivered to attacker-controlled domains that shared a text prefix with a
registered callback URL, enabling full OAuth authorization-code interception and account
takeover for any client where an attacker could get a victim to follow a crafted link.
Reported externally before confirmed exploitation. *Trigger:* PR "feat(oauth): allow
redirect_uri suffix flexibility for mobile deep links" (MOBILE-114) changed the redirect_uri
comparison from exact match to prefix match to accommodate per-install nonce segments in
mobile deep links. *Why it wasn't caught pre-merge:* the change reads as a narrow,
well-motivated compatibility fix and the existing test suite has no test asserting
redirect_uri comparison is exact — `SecurityTests.cs` only covers PKCE verifier comparison,
not redirect_uri. *Fix:* reverted to exact match; mobile team redirected to a fixed
custom-scheme URI + `state`-carried nonce instead. *Follow-up:* the regression test above;
flag redirect_uri/PKCE/audience comparisons in the OAuth service as a reviewer checklist item
requiring a second reviewer with security context, since these are exactly the "5-line diff,
huge blast radius" changes generic code review tends to wave through.
