# Playtrough backend

ASP.NET Core API (.NET 10) with Identity, Entity Framework Core and MySQL.

Run these commands from the repository root:

```powershell
dotnet build backend/Playtrough.sln --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet run --project backend/src/Playtrough.Api
```

Swagger is available at http://localhost:5000/swagger.

## Automated integration tests

The test project includes WebApplicationFactory tests using the real API startup,
Identity, repositories, token service and JWT bearer middleware. Each test owns an
isolated SQLite database in memory and a random JWT signing key. No Docker instance,
MySQL database or development credentials are needed.

```powershell
dotnet test backend/Playtrough.sln -c Release --disable-build-servers -m:1
```

Release allows these tests to run while the development API is open in Debug.
The `__tests/protected` controller is registered only by LoginApiFactory and is not
part of the production API. Tests cover actual lockout/counter updates and tokens
obtained through HTTP login. The expired-token case uses a valid test signature and
past timestamps, so it specifically verifies lifetime validation without waiting.

SQLite exercises persistence and the HTTP pipeline, but does not verify MySQL-specific
behavior or migrations. The full suite currently contains 78 passing cases.

`AuthServiceLoginTests` and `TokenServiceTests` retain the existing service coverage.
`AuthServiceLogoutTests` uses isolated SQLite databases and the real token service and
refresh-token repository to verify persisted revocation, idempotency, unknown tokens,
ownership and expiry. SQLite supports the repository's `ExecuteUpdateAsync`, unlike
EF InMemory. Identity dependencies that logout does not use are mocked with Moq.

`AuthEndpointsTests` verifies login by email/username, generic credential errors,
validation, lockout and counter reset; signed, tampered, unsigned and expired JWTs;
and logout responses (204/400/401), persisted revocation and repeated logout.

## Refresh rotation

POST /api/auth/refresh is public and requires a nonempty `refreshToken`. Successful
responses have the same token/user fields as login. Invalid, expired, revoked or
unusable-user tokens return the same 401 ProblemDetails. Missing tokens return 400;
unexpected persistence/generation failures return 500 without exposing token values.

Refresh reuses TokenService hashing/generation, RefreshToken.IsActive and JwtSettings.
Consumption, insertion and ReplacedByToken linkage commit in one transaction.
The first SQL operation in that transaction conditionally consumes the active token,
so concurrent refreshes cannot both succeed. Reusing a rotated token revokes the
user's active refresh tokens; reusing a logout-revoked token does not affect others.
The strict reuse policy also applies to concurrent requests: one gets 200 and one
gets 401, and the replay invalidates the winner's new refresh token. Access JWTs
remain valid until their configured expiry.

`AuthServiceRefreshTests` verifies rotation, hashing/lifetimes, current roles,
eligibility, reuse isolation, safe warning logs and transaction rollback.
`RefreshEndpointsTests` exercises the real HTTP flow and concurrency. Ordinary tests
reuse isolated in-memory SQLite; concurrency uses a temporary SQLite WAL database
with separate connections so requests do not share a transaction/connection.
Existing login/logout/register test cases remain unchanged.

MySQL was checked with 28 assertions, including three concurrent request pairs,
RevokedAt/ReplacedByToken persistence, generic errors and logout-session isolation.
The unique temporary account and its tokens were removed afterward. Existing
RefreshTokens fields suffice; no migration is required.

## Swagger authentication examples

POST /api/auth/login documents login by email and username as selectable examples,
plus JSON response examples for 200, 400, 401, 423 and 500. It explains required fields,
generic credential errors, lockout and default token lifetimes. Example token values
are illustrative and cannot authenticate requests. Use an existing registered account
when executing requests from Swagger UI.

POST /api/auth/refresh documents its public contract (no Authorization header),
required refreshToken, rotation, default lifetimes, current user roles and replay
detection, including the strict concurrent-request behavior. Selectable request
examples cover renewal, missing token (400) and nonexistent token (401), with JSON
response examples and typed schemas for 200/400/401/500. Replace the illustrative
token with the current token from login or the last refresh. Save both new tokens;
reusing an old rotated token revokes that user's active refresh tokens.

SwaggerDocumentationTests checks the generated refresh contract and examples while
preserving the login/register examples.

## Configuration

The API keeps its existing UserSecretsId, so local database credentials and the JWT
signing key remain available after the project rename. Set `ConnectionStrings:DefaultConnection`
and `Jwt:Key` through user-secrets, or use `ConnectionStrings__DefaultConnection` and
`Jwt__Key` environment variables. Never commit secrets.

JWT issuer/audience are now `Playtrough` and `PlaytroughClient`. Previously issued
tokens require a new login after restarting the API.

## Existing local database

The database `playtrought_dev`, Docker container `playtrought-mysql` and Compose volume
key `playtrought_mysql_data` retain their original infrastructure identifiers to keep
using the existing data. They are not public application names. No database objects,
data, migration IDs or volume contents are renamed by the application name correction.

To start MySQL using the existing local `backend/.env`:

```powershell
docker compose -f backend/docker-compose.yml up -d --wait
```
