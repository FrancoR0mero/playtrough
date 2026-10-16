# US-04: backend refresh rotation

- Follows Controllers / Dtos/Auth / Services / Repositories without reorganizing files.
- Reuses TokenService.HashRefreshToken, TokenService.CreateAsync, RefreshToken.IsActive,
  LoginUserDto and JwtSettings (15 minutes / 7 days by default).
- Public POST /api/auth/refresh: 200, 400, uniform 401; unexpected errors use 500 ProblemDetails.
- Transactional conditional consumption, hashed replacement insertion and linkage.
- Rolled-back generation failures preserve the original token and insert no replacement.
- Reuse of a rotated token revokes only that user's active refresh tokens and logs
  a warning without raw tokens or hashes. Logout revocation does not trigger global revocation.
- Strict concurrent reuse: exactly one 200 and one 401; winner's replacement is invalidated.
- xUnit/Moq and existing helpers reused; 25 added cases, 77 total.
- Concurrency uses isolated temporary SQLite WAL storage and distinct connections;
  other tests use SQLite in memory. No MySQL or developer credentials required for tests.
- Manual MySQL verification: 28 checks passed, including three concurrent pairs;
  RevokedAt and ReplacedByToken confirmed. Temporary account/tokens cleaned up.
- No frontend, Swagger customization, refresh job, rate limiting or new roles feature.
- Existing entity/migration already contains all fields; no schema change needed.
