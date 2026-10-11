# Task: US1 Register frontend + Swagger docs (email + username + password)

**Branch**: `feat/sprint1-Register-User`
**Status**: completed
**Authorized by**: user request 2026-10-11 — frontend register form (Tailwind), reactive validations, server error display, unit tests, Swagger examples.

## Objective

Finish US1 on the client + API docs: a registration form at `/auth/register` (Spanish UI) wired to `POST /api/auth/register`, with reactive validations mirroring the backend, server error mapping (201/400/409), Vitest unit tests for the async register service, and Swagger request/response examples (own filter, NO new packages).

## Out of scope

- Login, JWT wiring, `jwt-interceptor`/`auth-guard`/`role-guard` (placeholders — untouched), OAuth.
- New frontend/backend packages (Swagger examples via own `IOperationFilter`; UI via Tailwind v4 already installed).
- Backend behavior changes (except the Swagger docs filter + its registration).

## Environment facts

- **Frontend**: Angular 22 (standalone by default), signals, typed reactive forms (`@angular/forms` present), Tailwind CSS v4 (`tailwindcss@4.1.12` + `@tailwindcss/postcss`, CSS-first: `@import "tailwindcss"` in `src/styles.css` — verified present), Vitest 5 as test runner (`ng test`, app.spec.ts exists), ESLint/Prettier configured.
- `frontend/src/environments/environment.development.ts` → `apiUrl: 'http://localhost:5000/api'`.
- `frontend/src/app/app.routes.ts` → was `Routes = []` (EMPTY). Register route added.
- `frontend/src/app/app.html` → `<app-layout />`; layout component in `shared/components/layout/` exposes `<router-outlet />` (verified), so the lazy route renders inside the existing layout; layout behavior untouched.
- No `@angular/material` — Tailwind is the styling approach.
- Conventions (project skill scope-rule-architect-angular): standalone (no NgModules), no `.component`/`.service` suffixes in filenames, feature folder `features/<feature>/`, `inject()`, `input()`/`output()`, OnPush, signals + computed, native control flow `@if/@for`, typed reactive forms, no `any`, NO lifecycle hooks like `ngOnInit`, Scope Rule (code used by 1 feature stays local in the feature).
- **Backend**: `backend/src/Playtrought.Api` (net10.0, Swashbuckle `AddSwaggerGen` already configured). Controller `AuthController.Register` already returns 201 Created / 400 `ValidationProblemDetails` / 409 `ProblemDetails` / 500 Problem. Identity policy: 8+, upper, lower, digit, no symbol. Tables renamed (Users/Roles/..., applied).
- CORS AngularDev allows `http://localhost:4200` — no change needed.

## Decisions (final, user-approved 2026-10-11)

1. **Component**: `features/auth/register.ts` (RegisterComponent, selector `app-register`). NOT "login" — login is a future US; it will be `features/auth/login.ts`. Route: `/auth/register` lazy via `loadComponent`.
2. **UI language: Spanish** — labels, placeholders, per-field errors and server messages in Spanish. Code identifiers/comments stay English.
3. **Server error mapping**: 400 `ValidationProblemDetails` → per-field Spanish messages; 409 `ProblemDetails` → Spanish duplicate message (email vs username inferred from Title); 500/network → generic Spanish message.
4. **Swagger examples**: own `IOperationFilter` + static example class (Option A), zero new packages. Swashbuckle 10 (Microsoft.OpenApi 2.7.5) dropped `OpenApiAnyFactory` — examples are `System.Text.Json.Nodes.JsonNode`; the filter assigns `mediaType.Example = JsonNode.Parse(...)`.
5. **Success UX**: inline success panel (no redirect — login page does not exist yet).
6. **Service placement**: `features/auth/services/auth.ts` (auth-feature-local, Scope Rule); models in `features/auth/models/`.

## Tasks

### T1 — Register component (Tailwind + reactive form)
- [x] T1.1 `features/auth/register.ts` + `register.html` (+ `register.css` only if needed): typed `FormGroup` { email, username, password }, Tailwind v4 layout (centered card `max-w-md mx-auto`), labels + inputs + per-field error area + submit button with `submitting`/disabled state, success panel (green) after 201.
- [x] T1.2 Route in `app.routes.ts`: `{ path: 'auth/register', loadComponent: () => import('./features/auth/register').then(m => m.RegisterComponent) }` (verify layout exposes `<router-outlet />`; if not, handle routing containment correctly without touching layout behavior).
- [x] T1.3 Verify Tailwind v4 import in `src/styles.css`; add `@import "tailwindcss";` if missing.

### T2 — Reactive validations (mirror backend)
- [x] T2.1 Email: required + valid format. Username: required, min 3, max 20, pattern `^[a-zA-Z0-9_.]+$` (backend regex `^[a-zA-Z0-9_.]{3,20}$`). Password: required, min 8, at least one uppercase, one lowercase, one digit (mirror Identity policy).
- [x] T2.2 Spanish error messages per validator; show errors only for touched fields or after submit.

### T3 — AuthService + server error mapping
- [x] T3.1 `features/auth/services/auth.ts`: `register(payload: RegisterRequest): Observable<RegisterResponse>` → `POST ${environment.apiUrl}/auth/register`, JSON body `{ email, username, password }`. Injectable (`providedIn: 'root'` acceptable; feature-local placement).
- [x] T3.2 Error mapping to Spanish for: 400 `ValidationProblemDetails` (`error.errors` dictionary keyed Email/Username/Password — translate common backend messages: required/email format/username length+regex/password too short/upper/lower/digit), 409 `ProblemDetails` (Title "Email already registered" → "El email ya está registrado"; "Username already taken" → "El nombre de usuario ya está en uso"), 500/network → "Ocurrió un error inesperado. Inténtalo de nuevo.".
- [x] T3.3 Component consumes service with signals: `submitting`, `success`, `fieldErrors` (Record<string, string>), `formError` (string | null); disables submit while submitting.

### T4 — Unit tests (Vitest)
- [x] T4.1 `auth.service.spec.ts` (or co-located spec): TestBed + `provideHttpClientTesting` + `HttpTestingController` for `register()`:
  - [x] 201 → returns mapped `RegisterResponse`, verifies method POST, URL `/auth/register`, body serialization.
  - [x] 400 (ValidationProblemDetails with `errors.Password`) → error mapped to Spanish field message.
  - [x] 409 (Email) → Spanish duplicate message; 409 (Username) → Spanish duplicate message.
  - [x] 500 / network error → generic Spanish message.
- [x] T4.2 Run `ng test` (vitest) green; fix any pre-existing broken test only if it blocks the run (report it).

### T5 — Swagger examples (Opción A, no packages)
- [x] T5.1 `backend/src/Playtrought.Api/OpenApi/RegisterEndpointExamplesFilter.cs`: `IOperationFilter` that, ONLY for `AuthController.Register`, sets request body example + response examples for 201 / 400 / 409 (realistic JSON matching `RegisterRequestDto`, `RegisterResponseDto`, `ValidationProblemDetails`, `ProblemDetails`).
- [x] T5.2 Register the filter in `AddSwaggerGen` (`options.OperationFilter<RegisterEndpointExamplesFilter>()`).
- [x] T5.3 Build backend 0 warnings/0 errors; verify examples appear in `swagger.json` (confirmed by running the API and reading `/swagger/v1/swagger.json`).

### T6 — Verification and delivery
- [x] T6.1 Frontend: `ng test` green, `ng build` green (Tailwind included).
- [x] T6.2 Backend: `dotnet build backend/Playtrought.sln` 0/0.
- [x] T6.3 Work-unit commits (conventional, no AI attribution; English commit messages):
  1. `feat(auth): add register form with tailwind and reactive validations`
  2. `feat(auth): add auth service with server error mapping`
  3. `test(auth): add auth service register unit tests`
  4. `docs(api): add register endpoint request and response examples to swagger`
- [x] T6.4 Update this doc + report (files created/modified, verification results, examples).

## Evidence (delivered 2026-10-11)

**Frontend files created**
- `frontend/src/app/features/auth/register.ts` — RegisterComponent (standalone, OnPush, `inject()`), typed reactive form, custom password complexity validator (upper/lower/digit, no symbol), signals `submitting`/`success`/`submitAttempted`/`fieldErrors`/`formError`, Spanish per-field error map, service wiring.
- `frontend/src/app/features/auth/register.html` — Tailwind v4 card (`max-w-md mx-auto mt-16 p-8 border rounded-lg shadow-sm`), Spanish labels/placeholders, per-field red errors, green success panel, form-level error alert, disabled submit while invalid/submitting.
- `frontend/src/app/features/auth/models/register.ts` — `RegisterRequest`, `RegisterResponse`, `ValidationProblemDetails`, `ProblemDetails`.
- `frontend/src/app/features/auth/services/auth.ts` — `AuthService.register` (POST `/api/auth/register`), `RegisterError` typed error (fieldErrors + formError), Spanish translations for known backend/Identity messages, generic 500/network fallback.
- `frontend/src/app/features/auth/services/auth.spec.ts` — 5 Vitest cases (201 mapping + POST/URL/body, 400 Password field, 409 Email, 409 Username, 500 generic).
- Modified: `frontend/src/app/app.routes.ts` (lazy `auth/register` route). `styles.css` already had `@import 'tailwindcss';` — untouched.

**Backend files created**
- `backend/src/Playtrought.Api/OpenApi/RegisterEndpointExamplesFilter.cs` — `IOperationFilter` scoped to `AuthController.Register`; sets JsonNode examples for request body, 201, 400, 409 (guards nullable `Content`/`Responses` per Microsoft.OpenApi 2.7.5 annotations).
- Modified: `backend/src/Playtrought.Api/Program.cs` — `AddSwaggerGen(options => options.OperationFilter<RegisterEndpointExamplesFilter>())`.

**Verification (exact outputs)**
- `npx ng test --watch=false` → `Test Files 2 passed (2)`, `Tests 7 passed (7)` (5 new + 2 pre-existing app.spec).
- `npx ng build` → success; initial total 244.72 kB, register lazy chunk 46.49 kB, styles 10.89 kB (Tailwind CSS emitted).
- `dotnet build backend/Playtrought.sln` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- Live check: `GET http://localhost:5000/swagger/v1/swagger.json` → request example `{"email":"ana@example.com","username":"ana_dev","password":"Str0ngPass1"}`, 201/400/409 examples present, incl. `"title":"Email already registered"`. API process stopped afterwards.

**Commits (new, in order)**
1. `01f3cfd feat(auth): add register form with tailwind and reactive validations`
2. `cec1735 feat(auth): add auth service with server error mapping`
3. `49a10e3 test(auth): add auth service register unit tests`
4. `4b1c972 docs(api): add register endpoint request and response examples to swagger`

Note: an additional inherited commit `e910a9a refactor(repositories): move repository contracts into Interfaces and Implementations namespaces` records pre-existing uncommitted backend changes (repository split into Interfaces/Implementations) that were present in the worktree before this task; it is required so the worktree ends clean and each commit's tree builds.