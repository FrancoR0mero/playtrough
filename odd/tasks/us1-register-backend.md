# Task: US1 Register backend (email + username + password)

**Branch**: `feat/sprint1-Register-User`
**Status**: in progress
**Authorized by**: user request 2026-10-10 — US1 register endpoint, backend only.

## Objective

Implement `POST /api/auth/register` so a visitor can register with email, username and password. Acceptances: unique email/username, password policy applied, password hashed only by Identity (never by hand), new user gets role `User`.

## Out of scope (explicit)

- Login, JWT issuance/validation, email confirmation, password recovery (other USs).
- Frontend, Angular Material.
- New packages (FluentValidation, AutoMapper, MediatR require explicit user approval first).
- Manual password hashing; manual SQL; auth middleware.

## Environment facts

- `net10.0`; API `backend/src/Playtrought.Api`, tests `backend/tests/Playtrought.Tests` (xUnit + Moq, no test source yet).
- Identity already wired: `AddIdentity<ApplicationUser, IdentityRole<int>>(RequireUniqueEmail=true)` + EF stores; role seed `User`(1) / `Admin`(2); unique indexes on `NormalizedEmail` and `NormalizedUserName`.
- CORS `AngularDev` already allows `http://localhost:4200`; `RequireUniqueEmail` stays on.
- Convention: namespace per folder (`Playtrought.Api.Models`, `Playtrought.Api.Data`). New folders: `Controllers`, `Dtos/Auth`, `Repositories`, `Services`.
- MySQL Docker on 3307 (`playtrought_dev`); user-secrets `ConnectionStrings:DefaultConnection` set.
- EF tools default to Development; `dotnet ef` works without env var here.

## Decisions (with rationale, one line each)

1. **No ConfirmPassword in DTO**: password-match validation is a client concern; echo from the client adds no server-side security.
2. **Transaction via `IDbContextTransaction` exposed by the repository (`BeginTransactionAsync`)**: simplest testable atomicity for create-user + add-role on the same DbContext; service commits or rolls back.
3. **Password policy without required symbol**: length dominates strength (NIST SP 800-63B discourages forced composition); 8 + upper + lower + digit is reasonable and UX-friendly.
4. **`CreatedAt` as C# initializer `= DateTime.UtcNow`**: EF always writes the value on insert; column `datetime(6) NOT NULL` with no SQL default keeps the migration simple and deterministic.
5. **`Dtos/Auth` folder, namespace `Playtrought.Api.Dtos.Auth`**: follows the folder-per-namespace convention (like `Models`).
6. **Duplicate checks use `UserManager.NormalizeEmail`/`NormalizeName` against `Normalized*` columns**: case-insensitive by construction; DB unique indexes remain the second line of defense against races.
7. **`CreateAsync` `IdentityError`s `DuplicateEmail`/`DuplicateUserName` map to 409 too**: race-defense; the check is not the only guard.
8. **Mapper as simple static extension methods**: no AutoMapper (not present; user approval required before adding).
9. **201 via `Created("/api/auth/register", response)`**: no GET resource exists; the URI points to the endpoint that created the resource.
10. **400 uses `ValidationProblemDetails`** (via `ValidationProblem(ModelState)`); Identity password-policy errors are folded into the `Password` field.
11. **409/500 use plain `ProblemDetails`** with no internal details; unexpected failures are logged via `ILogger` without passwords or sensitive data.
12. **Service returns typed `ServiceResult<T>`** with error codes `EmailAlreadyExists`, `UsernameAlreadyExists`, `ValidationError` (weak password/validation), `Unexpected`; no exceptions for expected failures.
13. **Identity tables use domain names, not `AspNet*`** (`Users`, `Roles`, `UserRoles`, `RoleClaims`, `UserClaims`, `UserLogins`, `UserTokens`). User convention (2026-10-11): keep this naming in ALL future work. Implemented via `ToTable` + a `RenameTable` migration (data-preserving); FKs/indexes/PKs renamed too so the schema stays consistent with EF snapshots.

## Tasks

### T1 — Model + migration
- [x] T1.1 Add `CreatedAt { get; set; } = DateTime.UtcNow;` to `ApplicationUser` (no business logic, no validation attributes in entities).
- [x] T1.2 Generate migration `AddUserCreatedAt` (do NOT apply yet; user must see its content first).
- [x] T1.3 Report the migration's Up() content (column type, nullable) for the user to review before applying.

### T2 — DTOs (`Dtos/Auth`)
- [x] T2.1 `RegisterRequestDto`: Email (Required + EmailAddress), Username (Required, 3..20, `^[a-zA-Z0-9_.]{3,20}$`), Password (Required).
- [x] T2.2 `RegisterResponseDto`: Id, Username, Email, CreatedAt. Never hash/security stamp/internal Identity data.
- [x] T2.3 Mapping extensions entity↔`RegisterResponseDto` (simple static extension, no AutoMapper).

### T3 — Repository (`Repositories/`)
- [x] T3.1 `IUserRepository`: `ExistsByEmailAsync`, `ExistsByUsernameAsync`, `CreateAsync(user, password) → IdentityResult`, `AddToRoleAsync(user, role) → IdentityResult`, `BeginTransactionAsync() → IDbContextTransaction`.
- [x] T3.2 `UserRepository` wrapping `UserManager<ApplicationUser>` + `AppDbContext`. Duplicate checks on normalized columns. No business logic.

### T4 — Service (`Services/`)
- [x] T4.1 `ServiceResult<T>` + `AuthError` enum (None, EmailAlreadyExists, UsernameAlreadyExists, ValidationError, Unexpected).
- [x] T4.2 `IAuthService` / `AuthService.RegisterAsync(dto)`: trim → unique email → unique username → create with password → add role `User` → map response. Atomic create+role via transaction (rollback on any failure). Log unexpected failures with `ILogger` (no secrets).

### T5 — Controller + config
- [x] T5.1 `AuthController` `[ApiController]`, `POST api/auth/register`: 201 Created, 400 ValidationProblemDetails, 409 Conflict ProblemDetails, 500 Problem (generic). No business logic.
- [x] T5.2 `Program.cs`: password policy (8+, upper, lower, digit; no symbol), `AddScoped<IUserRepository, UserRepository>()`, `AddScoped<IAuthService, AuthService>()`; keep `RequireUniqueEmail=true`; CORS check (already OK).

### T6 — Tests
- [x] T6.1 `AuthServiceTests` (xUnit + Moq, repo mocked): success, duplicate email, duplicate username, weak password (IdentityResult errors), role-assign failure triggers rollback (no orphan user).
- [x] T6.2 Integration endpoint test: propose only (no test DB configured).
- [x] T6.3 `dotnet build` 0 warnings/errors; `dotnet test` green.

### T7 — Verification and delivery
- [x] T7.1 Do NOT run `dotnet ef database update` — user must approve the migration content first. (MIGRATION `RenameIdentityTablesAndAddUserCreatedAt` APPROVED + APPLIED 2026-10-11.)
- [x] T7.2 Commit work units (conventional, no AI attribution):
  - `ce21dbc` feat(identity): add CreatedAt to ApplicationUser (+ migration `AddUserCreatedAt`)
  - `b23c29b` feat(auth): add register DTOs, repository and service
  - `ff93797` feat(auth): add register endpoint and password policy
  - `48a2d8c` test(auth): add AuthService unit tests
  - (this commit) docs(odd): record US1 register verification evidence
- [x] T7.3 Report: files created/modified, build+test results, migration content, examples for Swagger (201/400/409).

## Verification evidence (2026-10-11)

- `dotnet build backend/Playtrought.sln` → Build succeeded, **0 warnings / 0 errors**.
- `dotnet test backend/Playtrought.sln` → **Passed: 7, Failed: 0, Skipped: 0** (duration ~552 ms). Tests: success+commit, trim, duplicate email, duplicate username, weak password (ValidationError + rollback), create race DuplicateEmail (→ EmailAlreadyExists + rollback), role-assign failure (Unexpected + rollback).
- Migration `20261011194139_RenameIdentityTablesAndAddUserCreatedAt` **APPLIED with user approval**: 7× `RenameTable` (AspNet* → Users/Roles/UserRoles/RoleClaims/UserClaims/UserLogins/UserTokens) + index/FK/PK renames + `AddColumn CreatedAt` (`datetime(6)` NOT NULL). No `DropTable`; roles User(1)/Admin(2) preserved; verified via `SHOW TABLES` + `information_schema`. DB default `0001-01-01 00:00:00` on CreatedAt is a one-time safety default for pre-existing rows (app always writes the value via C# initializer).
- Container `playtrought-mysql` healthy again after environment restart (docker compose up -d); DB reachable for the apply step.
- RDD native review of this slice: **pending** — `gentle-ai review assess` to run against a work-unit boundary after the user-facing delivery steps.