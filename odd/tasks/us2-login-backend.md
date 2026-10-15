# Task: US2 Login backend

**Branch**: `feat/sprint1-Register-User`
**Status**: implemented and verified locally
**Authorized by**: user-confirmed plan, 2026-10-14.

## Scope and conventions

Keep the existing controller -> service -> repository structure, folder namespaces,
`*Dto` names, `ServiceResult<T>` and `AuthError`. Reuse Identity, seeded User/Admin
roles, MySQL provider and installed JWT dependencies. No new packages or reorganization.

## Behavior

- `POST /api/auth/login`: required identifier/password; identifiers containing `@`
  resolve by normalized email, otherwise by normalized username. Trim identifier only.
- `SignInManager.CheckPasswordSignInAsync(..., lockoutOnFailure: true)` handles passwords.
  Identity explicitly allows lockout for new users: five failures, fifteen minutes.
  Successful login explicitly resets the failure counter before issuing tokens.
- 200: accessToken, refreshToken, expiresAt, user { id, username, email, role }.
- 400: ValidationProblemDetails. Invalid credentials and missing users share the same
  401 title/detail. Locked accounts return 423, including the fifth failed attempt.
  Other failures return generic 500 ProblemDetails. Responses containing tokens use no-store.
- JWT: HS256, fifteen minutes, sub/unique_name/email/role plus jti. All assigned roles
  appear in the JWT; the singular response role prioritizes Admin, then the first role
  alphabetically, or an empty string if a legacy account has no roles.
- Refresh token: 64 random bytes encoded as Base64, seven days, only SHA-256 hex
  persisted. ReplacedByToken is reserved for the replacement token's hash, never its
  plaintext. Refresh/rotation/logout endpoints are outside this task.
- JwtSettings uses Options with validation at startup. JWT authentication validates
  issuer, audience, HS256 signature and expiration with zero clock skew. Explicit bearer
  defaults override Identity's cookie defaults. Authentication precedes authorization.
- No signing key is in appsettings. A random development key was stored in user-secrets.
  Production can supply `Jwt__Key`; durations are `Jwt__AccessTokenExpirationMinutes`
  and `Jwt__RefreshTokenExpirationDays`. Issuer/audience use the same Jwt configuration.

## Database

Migration `20261014182210_AddRefreshTokens` applied successfully to local MySQL 8.4
(`playtrought_dev`, port 3307) and verified in `__EFMigrationsHistory`.
It adds only RefreshTokens: int identity primary key, UserId FK to Users (cascade delete),
unique TokenHash index, UserId index, UTC creation/expiration and nullable revocation/
replacement fields. Existing Identity tables and role seed are preserved.

## Verification (2026-10-14)

- `dotnet build backend/Playtrough.sln --no-restore --disable-build-servers -m:1 -v minimal`:
  succeeded, zero warnings/errors.
- `dotnet test backend/Playtrough.sln --no-build --no-restore --disable-build-servers -m:1 -v minimal`:
  28 passed, zero failed/skipped, including all seven existing registration tests.
  VSTest requires execution outside the restricted sandbox for its testhost connection.
- Live API on temporary port 15000, real Identity and MySQL: 24 checks passed.
  Verified both login identifiers, no-store, JWT signature/claims/issuer/audience/expiry,
  response expiry, persisted hashes and exact seven-day refresh lifetime, generic 401,
  failure-counter increment/reset, required-field 400, fifth-attempt lockout, fifteen-minute
  lockout in MySQL, 423 with a correct password while locked, no token rows on failure,
  and successful login after expiration. Test user and its tokens were removed afterward.
- `Playtrough.Api.http` contains manual examples, including separate registration for
  the lockout fixture. Run the bad-password request five times to reach 423.

## Local operation

Development signing key lives in the API project's existing user-secrets store.
Configure a fresh environment with a cryptographically generated key of at least 32 bytes,
the existing ConnectionStrings:DefaultConnection secret, and Docker's backend/.env.
Run `dotnet run --project backend/src/Playtrough.Api` (default URL localhost:5000).
Use Swagger or `backend/src/Playtrough.Api/Playtrough.Api.http` to exercise the endpoint.

Existing uncommitted Swagger filter and frontend task-document changes were preserved
and excluded from the login commits. No push was performed.

## Automated integration coverage (2026-10-15)

- Added Microsoft.AspNetCore.Mvc.Testing 10.0.13 and Microsoft.EntityFrameworkCore.Sqlite
  10.0.13 to the test project. Program exposes its entry point as public partial solely
  to support WebApplicationFactory; no production authentication logic changed.
- LoginApiFactory runs the real API pipeline, replacing only the database provider and
  supplying test-only JwtSettings. Each factory owns a private SQLite in-memory connection
  and an independently generated signing key. Identity, repositories, AuthService,
  TokenService and bearer authentication remain real.
- TestProtectedController is registered as an MVC application part only by the factory.
  Its endpoint requires authorization and the User role; it is absent from production.
- Six new integration cases verify: fifth-attempt lockout for fifteen minutes and rejection
  of a correct password while locked; successful login resets a persisted counter from
  three to zero and the next failure increments to one; real persisted refresh hashes
  with seven-day lifetime; login JWT authenticates sub/name/email/role on the protected
  endpoint; payload tampering returns 401; a correctly signed but expired token returns
  401; empty fields produce 400 ValidationProblemDetails through HTTP. Some assertions
  are grouped into the same case, with two separate cases in the invalid-token theory.
- `dotnet build backend/Playtrough.sln -c Release --no-restore --disable-build-servers -m:1`:
  successful, zero warnings/errors.
- `dotnet test backend/Playtrough.sln -c Release --no-build --no-restore --disable-build-servers -m:1`:
  34 passed, zero failed/skipped (28 prior cases plus six new integration cases).
- Release was used because the running development API locks its Debug executable.
  The development API remained running; no existing database or user data was modified.
- These tests verify HTTP/Identity/JWT integration with SQLite; MySQL-specific behavior
  and migration application remain separate verification concerns.
