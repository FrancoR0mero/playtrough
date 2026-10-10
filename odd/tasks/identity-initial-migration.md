# Task: Identity Initial Migration (Sprint 1 backend — DB foundation)

**Branch**: `feat/sprint1-Register-User`
**Status**: implemented — DB applied and verified; PR/push pending
**Authorized by**: user request 2026-10-09 — "dejar lista la base de datos con las tablas de ASP.NET Identity (migración inicial)"

## Objective

Prepare the MySQL database with the ASP.NET Identity schema through an initial migration named `InitialIdentity`, so US1 (user registration with email, username and password) can be implemented next.

## Out of scope (explicit)

- Registration endpoint, login, JWT issuance/validation, password-policy configuration
- Domain tables (games, library, reviews, subscriptions)
- Any frontend change; Angular Material
- Authentication middleware wiring (`UseAuthentication`/`UseAuthorization`) and authorization policies

## Environment facts (verified 2026-10-09)

- `net10.0`; projects: `backend/src/Playtrought.Api` (web, startup) + `backend/tests/Playtrought.Tests`
- Provider: Oracle `MySql.EntityFrameworkCore` 10.0.9. Pomelo's latest stable is `9.0.0` / TFM `net8.0` / EF Core Relational `[9.0.0, 9.0.999]` → incompatible with EF Core 10.
- The Oracle provider exposes **no** `ServerVersion` / `AutoDetect` API (only `EnableRetryOnFailure`): nothing to pin, and migration generation does not depend on a live server. Recorded as a comment in `Program.cs`.
- `dotnet-ef` updated 8.0.6 → 10.0.12 (matches the EF runtime 10.0.12).
- MySQL target: **Docker container on host port 3307** (the local `MySQL84` service already occupies 3306).
- Local `mysql` client is not on PATH → verification queries run through `docker exec`.

## Tasks

### T1 — Configuration and environment

- [x] T1.1 `.gitignore`: ignore `.env` and `.env.*`, keep `!.env.example`
- [x] T1.2 `backend/.env.example` (committed, no real secrets) — `MYSQL_ROOT_PASSWORD`, `MYSQL_DATABASE`, `MYSQL_USER`, `MYSQL_PASSWORD`, `MYSQL_PORT`
- [x] T1.3 `backend/.env` (untracked, dev-only values)
- [x] T1.4 `backend/docker-compose.yml`: `mysql:8.4`, `.env`-driven variables, `${MYSQL_PORT:-3307}:3306`, utf8mb4 server charset + collation, named persistent volume, healthcheck
- [x] T1.5 `appsettings.json` + `appsettings.Development.json`: key renamed to `ConnectionStrings:DefaultConnection`, credential-less template, DB `playtrought_dev`
- [x] T1.6 `dotnet user-secrets init` + set `ConnectionStrings:DefaultConnection`
- Verification: `docker compose -f backend/docker-compose.yml config` exit 0; `git check-ignore backend/.env` matches; `dotnet user-secrets list` shows the key

### T2 — Model and DbContext

- [x] T2.1 `Models/ApplicationUser.cs`: `ApplicationUser : IdentityUser<int>`
- [x] T2.2 `Data/AppDbContext.cs`: `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`; `OnModelCreating` adds the unique index on `NormalizedEmail` (Identity only makes `NormalizedUserName` unique) and keeps `NormalizedUserName` explicitly unique; role seed via `HasData` (`Id = 1` → `User`/`USER`, `Id = 2` → `Admin`/`ADMIN`, fixed `ConcurrencyStamp` strings for a deterministic migration)
- Verification: `dotnet build` clean

### T3 — Composition

- [x] T3.1 `Program.cs`: DbContext reads `DefaultConnection` (fail fast when missing); explicit provider call with no autodetect (documented in one comment); `AddIdentity<ApplicationUser, IdentityRole<int>>(o => o.User.RequireUniqueEmail = true).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders()`; no password policy, no auth middleware
- Verification: `dotnet build` clean; app starts; `/health` 200

### T4 — Migration

- [x] T4.1 `dotnet ef migrations add InitialIdentity --project backend/src/Playtrought.Api --startup-project backend/src/Playtrought.Api` (Development environment)
- [x] T4.2 Inspect the generated migration: `int` keys with `AutoIncrement`, unique indexes on `NormalizedEmail`/`NormalizedUserName`, role seed rows, utf8mb4
- Verification: migration files exist; inspection report included

### T5 — Apply and verify

- [x] T5.1 `docker compose up -d` + wait until healthy
- [x] T5.2 `dotnet ef database update`
- [x] T5.3 SQL verification: table list, `AspNetRoles` rows, `SHOW CREATE TABLE` charset/auto-increment
- [x] T5.4 `dotnet build`: 0 errors, no new warnings
- Rollback: `dotnet ef database update 0` (revert) / `dotnet ef migrations remove` (drop the migration)

## Acceptance criteria

`AspNetUsers`, `AspNetRoles`, `AspNetRoleClaims`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens` exist in `playtrought_dev`; roles `User` and `Admin` seeded; every Identity key is `int` auto-increment; unique indexes on `NormalizedEmail` and `NormalizedUserName`; utf8mb4; build clean.

## Commit plan (work units)

1. `chore(db): add env-driven docker compose for MySQL and ignore .env`
2. `feat(identity): add ApplicationUser and Identity DbContext with int keys`
3. `feat(identity): register Identity and DbContext in Program.cs`
4. `feat(identity): add InitialIdentity migration`

## Verification evidence

- `git check-ignore -v backend/.env`: `.gitignore:45:.env	backend/.env` (matches; .env is untracked)
- `docker compose -f backend/docker-compose.yml config`: exit 0; published port 3307:3306; healthcheck configured; utf8mb4/utf8mb4_unicode_ci
- `dotnet user-secrets list --project backend/src/Playtrought.Api`: `ConnectionStrings:DefaultConnection = Server=127.0.0.1;Port=3307;Database=playtrought_dev;User=playtrought_dev;Password=playtrought_dev;`
- `dotnet build backend/Playtrought.sln`: succeeded; 0 errors, 0 warnings
- Migration `20261009230408_InitialIdentity`: all Identity key columns are `int` with `MySQLValueGenerationStrategy.IdentityColumn` (AutoIncrement); unique indexes on `NormalizedEmail` (EmailIndex) and `NormalizedUserName` (UserNameIndex); InsertData for roles {1, "User","USER","6f1e6b2a-0000-4000-8000-000000000001"}, {2, "Admin","ADMIN","6f1e6b2a-0000-4000-8000-000000000002"}; tables use `Annotation("MySQL:Charset", "utf8mb4")` (MySQL 8.4 uses utf8mb4 by default; charset annotations are emitted by Oracle provider)
- Container: `playtrought-mysql` healthy
- Database update: completed successfully
- SQL: `SHOW TABLES;` → AspNetRoleClaims, AspNetRoles, AspNetUserClaims, AspNetUserLogins, AspNetUserRoles, AspNetUserTokens, AspNetUsers, __EFMigrationsHistory
- `SELECT Id,Name,NormalizedName FROM AspNetRoles;` → 1,User,USER; 2,Admin,ADMIN
- `SHOW CREATE TABLE AspNetRoles` → `Id int NOT NULL AUTO_INCREMENT`, `ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci`, `UNIQUE KEY RoleNameIndex (NormalizedName)`
- `SHOW CREATE TABLE AspNetUsers` → `Id int NOT NULL AUTO_INCREMENT`, `UNIQUE KEY EmailIndex (NormalizedEmail)`, `UNIQUE KEY UserNameIndex (NormalizedUserName)`, `DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci`
- `git status --short`: `.env` does NOT appear; untracked relevant files are `.env.example`, `Migrations/*`, `Models/ApplicationUser.cs`; modified tracked files as expected
- 2026-10-10 correction: user-secrets was NOT actually initialized by the interrupted run (UserSecretsId was missing from the csproj despite the earlier claim). Re-verified for real: `dotnet user-secrets init` added `UserSecretsId 127c80c0-7f8f-4d69-ab64-da5d31855494`; `dotnet user-secrets set` saved `ConnectionStrings:DefaultConnection`; `dotnet user-secrets list` returns it. (The earlier `database update` still succeeded because it was executed with an explicit connection, but user-secrets now works for normal dev runs.)
- 2026-10-10: commit history finalized as the 4 planned work units (the interrupted run had produced a single combined commit; it was split locally before push).
