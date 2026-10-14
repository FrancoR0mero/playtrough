# Task: US2 Login frontend

**Branch**: `feat/sprint1-Register-User`
**Status**: implemented and verified
**Authorized by**: user-confirmed plan, 2026-10-14.

## Scope and existing conventions

Reuse RegisterComponent and its existing register.ts/register.html files for both
authentication modes. Preserve registration rules, AuthService, feature-local models
and services, standalone components, signals, reactive forms, OnPush and Tailwind.
No new packages or folder reorganization.

## Delivered behavior

- /auth/login and /auth/register load the same component with route mode data.
- Separate login form requires identifier/password only. No registration password policy
  applies to login. Identifier is trimmed before sending; password is preserved exactly.
- Touched-field errors, password visibility button, autocomplete, label associations,
  aria-invalid/describedby, role=alert, loading spinner and duplicate-submit prevention.
  Standard form submission supports Enter; failed validation focuses the first invalid field.
- AuthService.login returns Observable<LoginResponse> using environment.apiUrl.
  Typed models: LoginRequest, LoginResponse, AuthUser.
- TokenStorageService alone saves/reads/clears the complete session under
  localStorage key playtrough.session. Its security tradeoff is documented in source for
  the thesis. Other localStorage keys are preserved.
- AuthService exposes readonly currentUser/isAuthenticated signals and eagerly restores
  the session at app initialization. Expired, malformed or incomplete stored sessions
  are discarded. An expiration timer also clears sessions while the app stays open.
- 401: "Credenciales incorrectas" below the form, no field-specific indication.
  423: temporary lockout with a suggestion to retry later. 400: field errors from the
  server, with unknown/global field messages shown at form level. Network/5xx or storage
  failures: generic retryable message. Editing a field clears its previous server error.
- Successful login redirects using a local returnUrl, otherwise /library. External URLs,
  auth-loop destinations and auxiliary outlets fall back to /library. Unknown routes use
  the default route. Signed-in users visiting either auth mode are redirected out.
- Existing AuthGuard protects /library and preserves returnUrl for guests. Library is a
  minimal landing view with username and a local logout action; no game functionality
  was introduced. The root and unknown routes redirect to /library.
- Document language is Spanish; branding stays Playtrough.

## Decisions

- Keep the existing RegisterComponent name/files to avoid reorganizing prior work.
- Keep storage in features/auth/services beside the existing AuthService, following the
  frontend's feature-based layout.
- Access-token expiry ends the local session. Refresh tokens are stored, but automatic
  renewal is not attempted because the backend has no refresh endpoint yet.
- The client restores validated stored data for UI state; server authorization remains
  responsible for validating tokens. No new API endpoint or backend change was needed.

## Verification

- npm run build: successful production build, within configured budgets.
- npm run lint: all files pass.
- npm test -- --watch=false: 43 tests passed across six files, including the original
  seven tests. HTTP is mocked through Angular HttpTestingController; routing uses
  RouterTestingHarness. Tests ran outside the sandbox because its temporary-file access
  can interfere with Angular/Vitest.
- Coverage includes login request/persistence/restore/expiration/logout, corrupt storage,
  storage access failures, weak login passwords, registration regression, touched errors,
  visibility toggle, duplicate submission, 400/401/423/network/500, retry, focus, safe
  returnUrl and routing with a restored session.

## Manual verification

1. From the repository root, start the API with
   `dotnet run --project backend/src/Playtrough.Api`. Start Angular in another terminal
   with `npm --prefix frontend start`. MySQL and the existing backend secrets must be ready.
2. Open http://localhost:4200/auth/register. Create a test account, for example
   ana_login@example.com / ana_login / Str0ngPass123, then use the Iniciar sesión link.
3. Login with email, then logout and repeat with username. Expect /library and a welcome
   message. Test the password visibility button and submitting with Enter.
4. Reload /library: the session should remain. Opening /auth/login or /auth/register with
   an active session should return to /library. Closing the session clears stored data.
5. From a logged-out state, enter a wrong password: expect only "Credenciales incorrectas"
   under the form and a usable retry button. An unknown username gets the same message.
6. To test lockout from a fresh counter, first login successfully and logout. Submit an
   incorrect password five times for that account: the fifth and subsequent attempts show
   the temporary-lockout message, including when the correct password is supplied.
   Wait fifteen minutes or use another account for other login checks.
7. Logged out, stop the API and submit valid-looking credentials: expect a generic error,
   no navigation and an enabled retry button. Restart the API and retry with an unblocked account.
8. Logged out, open /library?tab=playing: expect login with returnUrl. After successful
   login, the destination should retain /library?tab=playing. Try an external returnUrl
   or /auth/login as returnUrl: both should fall back to /library.

## Commits

- feat(auth): add login mode to existing authentication form
- feat(auth): persist and restore reactive login sessions
- feat(auth): handle login errors and authenticated navigation

All commits are local; no push is part of this task.
