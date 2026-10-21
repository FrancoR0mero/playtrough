# Frontend

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.2.2.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test --watch=false
```

Authentication tests use the existing Vitest runner, Angular TestBed and
HttpTestingController. The full suite currently has 117 passing cases.
Service tests cover login/session restoration and logout, including the bearer
header, server/network failures, missing refresh tokens and the five-second timeout.
Storage tests cover persistence, cleanup and corrupt or expired data.

Login and header tests use mocked AuthService and TokenStorageService dependencies;
Router navigation is spied while its URL parsing remains real. Login tests live in
`login.spec.ts`; existing registration cases remain in `register.spec.ts`.
HTTP tests verify there are no pending requests, and authentication storage is
cleared between tests. No backend or browser E2E server is needed.

AuthService has no public `clearSession()` method. Cleanup is verified through
`logout()`, refresh failures and TokenStorageService's `clear()`.

## Session refresh

`AuthService.refresh()` calls the public refresh endpoint without Authorization and
returns an Observable of the new access token. It replaces both stored tokens and
the user, including the rotated refresh token. Concurrent subscribers share one
request; a completed/failed request is released so a later call can start another.
Stale refresh responses cannot restore a logged-out session or overwrite a newer login.

HTTP 400/401/5xx and local persistence failures clear the session and redirect to
login. A 401 adds `sessionExpired=true`, which shows an accessible notice dismissed
on form submission or switching to registration. Network failures (HTTP status 0)
are propagated without clearing credentials, allowing an explicit later retry.

Expired access tokens do not authenticate a user, but stored refresh credentials
are retained for renewal. `getAccessToken()` returns the current stored access token;
`isAccessTokenExpired(leewaySeconds = 30)` checks expiresAt with a configurable
warning window (0 checks exact expiry). Refresh is explicit: no interceptor,
automatic retry, guard changes or scheduled refresh is included.

`auth-refresh.spec.ts` covers HTTP contracts, token replacement, error handling,
concurrency, terminal callback retries, expiry and stale response protection.

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
