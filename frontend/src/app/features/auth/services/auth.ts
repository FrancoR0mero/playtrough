import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, tap, throwError } from 'rxjs';
import type { LoginRequest, LoginResponse } from '../models/login';
import { TokenStorageService } from './token-storage';
import { environment } from '../../../../environments/environment';
import type {
  ProblemDetails,
  RegisterRequest,
  RegisterResponse,
  ValidationProblemDetails,
} from '../models/register';

/**
 * Typed failure raised by AuthService.register. The component can rely on this single
 * error shape instead of inspecting raw HttpErrorResponse values.
 */
export class RegisterError extends Error {
  constructor(
    readonly fieldErrors: Record<string, string>,
    readonly formError: string | null,
  ) {
    super(formError ?? 'Registration failed');
    this.name = 'RegisterError';
  }
}

const GENERIC_ERROR = 'Ocurrió un error inesperado. Inténtalo de nuevo.';
const FIELD_FALLBACK = 'El valor introducido no es válido.';

/** Spanish translations for the known backend/Identity messages (400 ValidationProblemDetails). */
const BACKEND_MESSAGE_TRANSLATIONS: Record<string, string> = {
  'Email is required.': 'El email es obligatorio.',
  'A valid email address is required.': 'El email no es válido.',
  'Username is required.': 'El nombre de usuario es obligatorio.',
  'Username must be between 3 and 20 characters.':
    'El nombre de usuario debe tener entre 3 y 20 caracteres.',
  'Username may contain only letters, digits, underscore and dot.':
    'El nombre de usuario solo puede contener letras, números, guion bajo y punto.',
  'Password is required.': 'La contraseña es obligatoria.',
  'Passwords must be at least 8 characters.': 'La contraseña debe tener al menos 8 caracteres.',
  "Passwords must have at least one uppercase ('A'-'Z').": 'Debe incluir al menos una mayúscula.',
  "Passwords must have at least one lowercase ('a'-'z').": 'Debe incluir al menos una minúscula.',
  "Passwords must have at least one digit ('0'-'9').": 'Debe incluir al menos un número.',
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly storage = inject(TokenStorageService);
  private readonly session = signal<LoginResponse | null>(null);
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;

  readonly currentUser = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  constructor() {
    this.restoreSession();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.expiryTimer));
  }

  login(identifier: string, password: string): Observable<LoginResponse> {
    const payload: LoginRequest = { identifier: identifier.trim(), password };
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/login`, payload).pipe(
      tap((response) => {
        this.storage.save(response);
        this.setSession(response);
      }),
    );
  }

  restoreSession(): void {
    this.setSession(this.storage.read());
  }

  logout(): void {
    this.storage.clear();
    this.setSession(null);
  }

  private setSession(session: LoginResponse | null): void {
    clearTimeout(this.expiryTimer);
    this.session.set(session);
    if (session) {
      const remaining = Math.min(Date.parse(session.expiresAt) - Date.now(), 2_147_483_647);
      this.expiryTimer = setTimeout(() => this.restoreSession(), Math.max(0, remaining));
    }
  }

  /** Registers a new account; maps server failures to a typed RegisterError with Spanish messages. */
  register(payload: RegisterRequest): Observable<RegisterResponse> {
    return this.http
      .post<RegisterResponse>(`${environment.apiUrl}/auth/register`, payload)
      .pipe(
        catchError((error: HttpErrorResponse) => throwError(() => this.toRegisterError(error))),
      );
  }

  private toRegisterError(error: HttpErrorResponse): RegisterError {
    if (error.status === 400) {
      return new RegisterError(
        this.mapValidationErrors(error.error as ValidationProblemDetails),
        null,
      );
    }

    if (error.status === 409) {
      return new RegisterError({}, this.mapConflict(error.error as ProblemDetails));
    }

    return new RegisterError({}, GENERIC_ERROR);
  }

  private mapValidationErrors(problem: ValidationProblemDetails): Record<string, string> {
    const fieldErrors: Record<string, string> = {};
    const errors = problem.errors ?? {};

    for (const [backendField, messages] of Object.entries(errors)) {
      const field = backendField.toLowerCase();
      fieldErrors[field] = messages
        .map((message) => BACKEND_MESSAGE_TRANSLATIONS[message] ?? FIELD_FALLBACK)
        .join(' ');
    }

    return fieldErrors;
  }

  private mapConflict(problem: ProblemDetails): string {
    const title = problem.title ?? '';
    return title.includes('Email')
      ? 'El email ya está registrado.'
      : 'El nombre de usuario ya está en uso.';
  }
}
