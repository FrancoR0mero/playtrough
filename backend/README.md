# Playtrough backend

ASP.NET Core API (.NET 10) with Identity, Entity Framework Core and MySQL.

Run these commands from the repository root:

```powershell
dotnet build backend/Playtrough.sln --disable-build-servers -m:1
dotnet test backend/Playtrough.sln --no-restore --disable-build-servers -m:1
dotnet run --project backend/src/Playtrough.Api
```

Swagger is available at http://localhost:5000/swagger. Manual requests are in
`src/Playtrough.Api/Playtrough.Api.http`.

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
behavior or migrations. The full suite currently contains 34 passing cases.

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
