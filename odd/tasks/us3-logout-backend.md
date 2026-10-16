# Task: US3 Logout backend

**Branch**: `feat/sprint1-Register-User`
**Status**: implemented and manually verified
**Authorized by**: user-confirmed backend-only plan, 2026-10-15.

## Scope

Keep the existing controller -> service -> repository layers and namespaces. Implement
logout only. No refresh endpoint, rotation, access-JWT revocation list, all-device logout,
frontend work, new test cases or Swagger documentation.

## Implementation

- LogoutRequestDto in Dtos/Auth requires refreshToken with DataAnnotations.
- POST /api/auth/logout uses Authorize. The authenticated JWT's sub must parse as a
  positive integer; missing or invalid sub uses the existing bearer challenge (401
  ProblemDetails). No request-supplied user id is accepted.
- AuthService.LogoutAsync delegates the hash to ITokenService.HashRefreshToken. Login
  uses that same method. The existing UTF-8/SHA-256/uppercase-hex algorithm is unchanged,
  including the exact token bytes; opaque refresh tokens are never trimmed.
- RefreshTokenRepository.RevokeActiveAsync issues one EF ExecuteUpdateAsync query,
  conditioned on token hash, authenticated user id, null RevokedAt and future ExpiresAt.
  Zero affected rows is normal success. No existence, ownership or lifecycle state is
  exposed in the 204 response. Repeated/concurrent requests preserve the first timestamp.
- RefreshToken.IsActive(utcNow) is reusable for future renewal and returns false for
  revoked or expired tokens. The SQL predicate implements the same activity rule.
- The existing RevokedAt field and schema are reused; no migration is needed.
- Missing/blank refreshToken produces automatic 400 ValidationProblemDetails. Missing
  or invalid JWT produces the existing 401 ProblemDetails. Unexpected exceptions use
  the existing global ProblemDetails handler.
- A source comment explicitly documents that issued access JWTs remain valid until their
  configured expiry (15 minutes by default). Only the supplied refresh token is revoked.
- The two existing test construction helpers were updated only for the added repository
  constructor argument. No assertions or test cases were added, and no tests were run.

## Verification (2026-10-15)

- Release solution build: zero warnings/errors. Existing test sources also compile.
- EF has-pending-model-changes: no changes since the last migration.
- Live API on temporary localhost:15000 with real MySQL: 26 manual HTTP/SQL checks passed.
  RevokedAt was confirmed to exist. Checked successful revocation and persistence,
  repeat logout preserving RevokedAt, unknown/expired/foreign-user tokens returning 204,
  no changes to foreign tokens or other sessions, simultaneous logout calls returning
  204, missing/invalid JWT (401), valid signatures with missing/invalid sub (401),
  missing/blank refreshToken (400), and continued use of the existing access JWT.
- New verification users/tokens were isolated and removed after verification; existing
  account data was not changed. MySQL was restarted after it stopped during the initial
  verification attempt, then all checks completed successfully.

## Manual steps

1. Start MySQL and the API with the existing local secrets and configuration.
2. Create/login a dedicated logout_demo account and keep both returned tokens for the test.
3. Query RefreshTokens joined to Users for logout_demo: the latest token has RevokedAt NULL.
4. Send the correct logout: expect 204 and a UTC RevokedAt in MySQL.
5. Repeat the same request with the same access JWT: expect 204 and an unchanged timestamp.
6. Send an unknown refresh token: expect the same empty 204.
7. Omit Authorization: expect 401 ProblemDetails. Send {} with a valid JWT: expect 400.
8. Other sessions stay active; the access JWT remains valid until its original expiry.

No push is part of this task.
