import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import {
  Observable,
  catchError,
  defer,
  finalize,
  map,
  of,
  shareReplay,
  tap,
  throwError,
  timeout,
} from 'rxjs';
import type { LoginRequest, LoginResponse } from '../models/login';
import type { ConfirmEmailRequest, ResendConfirmationRequest } from '../models/email-confirmation';
import type { RefreshRequest, RefreshResponse } from '../models/refresh';
import type {
  AuthMessage,
  ForgotPasswordRequest,
  ResetPasswordRequest,
} from '../models/password-recovery';
import { TokenStorageService } from './token-storage';
import { environment } from '../../../../environments/environment';
import { authDestination } from '../auth-navigation';
import { AppRole, hasMinRole as roleMeetsMinimum } from '../../../core/authorization/roles';
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

export class LoginError extends Error {
  constructor(
    readonly fieldErrors: Record<string, string>,
    readonly formError: string | null,
    readonly code: string | null = null,
  ) {
    super(formError ?? 'Login failed');
    this.name = 'LoginError';
  }
}

const GENERIC_ERROR = 'Ocurrió un error inesperado. Inténtalo de nuevo.';
const FIELD_FALLBACK = 'El valor introducido no es válido.';

/** Spanish translations for the known backend/Identity messages (400 ValidationProblemDetails). */
const BACKEND_MESSAGE_TRANSLATIONS: Record<string, string> = {
  'Email is required.': 'El email es obligatorio.',
  'Email or username is required.': 'El email o nombre de usuario es obligatorio.',
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
  private readonly router = inject(Router);
  private readonly session = signal<LoginResponse | null>(null);
  private expiryTimer: ReturnType<typeof setTimeout> | undefined;
  private sessionVersion = 0;
  private refreshRequest: {
    version: number;
    stream: Observable<string>;
    failureVersion?: number;
  } | null = null;
  private expiredRedirectPending = false;

  readonly currentUser = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);

  hasRole(role: AppRole): boolean {
    return this.isAuthenticated() && this.currentUser()?.role === role;
  }

  hasMinRole(role: AppRole): boolean {
    return this.isAuthenticated() && roleMeetsMinimum(this.currentUser()?.role, role);
  }

  constructor() {
    this.restoreSession();
    inject(DestroyRef).onDestroy(() => clearTimeout(this.expiryTimer));
  }

  login(identifier: string, password: string): Observable<LoginResponse> {
    const payload: LoginRequest = { identifier: identifier.trim(), password };
    return this.http.post<LoginResponse>(`${environment.apiUrl}/auth/login`, payload).pipe(
      tap((response) => {
        this.storage.save(response);
        this.sessionVersion++;
        this.setSession(response);
      }),
      catchError((error: unknown) => throwError(() => this.toLoginError(error))),
    );
  }

  restoreSession(): void {
    this.setSession(this.storage.read());
  }

  /** May return an expired token; callers should also check isAccessTokenExpired(). */
  getAccessToken(): string | null {
    return (this.session() ?? this.storage.read(true))?.accessToken ?? null;
  }

  /** Default warning window is 30 seconds; pass 0 to check exact expiry only. */
  isAccessTokenExpired(leewaySeconds = 30): boolean {
    const session = this.session() ?? this.storage.read(true);
    if (!session) return true;
    return Date.parse(session.expiresAt) <= Date.now() + Math.max(0, leewaySeconds) * 1_000;
  }

  refresh({ redirectOnFailure = true }: { redirectOnFailure?: boolean } = {}): Observable<string> {
    // Resolve the shared request on subscription. Completed requests are never reused,
    // even when callers retain and subscribe again to an earlier returned Observable.
    return defer(() => {
      const observe = (request: NonNullable<typeof this.refreshRequest>) =>
        request.stream.pipe(
          catchError((error: unknown) => {
            // Guards return their own UrlTree. Other callers retain the shared expiry redirect.
            if (redirectOnFailure && request.failureVersion === this.sessionVersion)
              this.expireSession();
            return throwError(() => error);
          }),
        );
      if (this.refreshRequest?.version === this.sessionVersion) return observe(this.refreshRequest);
      const version = this.sessionVersion;
      const request: NonNullable<typeof this.refreshRequest> = {
        version,
        stream: undefined! as Observable<string>,
      };
      const release = () => {
        if (this.refreshRequest === request) this.refreshRequest = null;
      };
      request.stream = defer(() => {
        const tokens = this.storage.readTokens();
        if (!tokens) return throwError(() => new Error('No refresh token is stored.'));
        const payload: RefreshRequest = { refreshToken: tokens.refreshToken };
        // Public endpoint: an expired access token must never be attached here.
        return this.http.post<RefreshResponse>(`${environment.apiUrl}/auth/refresh`, payload).pipe(
          map((response) => {
            if (
              version !== this.sessionVersion ||
              this.storage.readTokens()?.refreshToken !== tokens.refreshToken
            )
              throw new Error('The session changed while refreshing.');
            this.storage.save(response);
            this.setSession(response);
            return response.accessToken;
          }),
        );
      }).pipe(
        catchError((error: unknown) => {
          // Transient HTTP failures retain the session for a later explicit retry.
          const transientFailure =
            error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
          if (version === this.sessionVersion && !transientFailure) {
            this.clearSession();
            request.failureVersion = this.sessionVersion;
          }
          return throwError(() => error);
        }),
        // Release before notifying completion/error, so a caller can immediately retry
        // or request another refresh from its terminal callback without reusing this one.
        tap({ complete: release, error: release }),
        finalize(release),
        // Keep one request alive if an individual caller unsubscribes: the server may
        // already have rotated the token, and other callers still need its response.
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.refreshRequest = request;
      return observe(request);
    });
  }

  clearSession(): void {
    this.sessionVersion++;
    this.storage.clear();
    this.setSession(null);
  }

  /** Shared by refresh and the interceptor: clear locally and avoid auth redirect loops. */
  expireSession(): void {
    this.clearSession();
    const currentUrl = this.router.url;
    if (
      /^\/(?:auth(?:\/|[?#]|$)|confirm-email(?:[/?#]|$)|reset-password(?:[/?#]|$))/i.test(
        currentUrl,
      ) ||
      this.expiredRedirectPending
    )
      return;
    const returnUrl = authDestination(this.router, currentUrl);
    const destination = this.router.createUrlTree(['/auth/login'], {
      queryParams: { returnUrl, sessionExpired: true },
    });
    this.expiredRedirectPending = true;
    void this.router
      .navigateByUrl(this.router.serializeUrl(destination), { replaceUrl: true })
      .finally(() => {
        this.expiredRedirectPending = false;
      });
  }

  logout(): Observable<void> {
    return defer(() => {
      // A refresh already in flight must not restore a session after logout.
      this.sessionVersion++;
      const tokens = this.storage.readTokens();
      if (!tokens) return of(undefined);

      return this.http
        .post<void>(`${environment.apiUrl}/auth/logout`, { refreshToken: tokens.refreshToken })
        .pipe(
          // An unresponsive API must not prevent local logout.
          timeout(5_000),
        );
    }).pipe(
      catchError(() => of(undefined)),
      finalize(() => {
        this.clearSession();
      }),
    );
  }

  forgotPassword(email: string): Observable<AuthMessage> {
    const payload: ForgotPasswordRequest = { email: email.trim() };
    return this.http.post<AuthMessage>(`${environment.apiUrl}/auth/forgot-password`, payload);
  }

  confirmEmail(email: string, token: string): Observable<AuthMessage> {
    const payload: ConfirmEmailRequest = { email: email.trim(), token };
    return this.http.post<AuthMessage>(`${environment.apiUrl}/auth/confirm-email`, payload);
  }

  resendConfirmation(email: string): Observable<AuthMessage> {
    const payload: ResendConfirmationRequest = { email: email.trim() };
    return this.http.post<AuthMessage>(`${environment.apiUrl}/auth/resend-confirmation`, payload);
  }

  resetPassword(
    email: string,
    token: string,
    newPassword: string,
    confirmPassword: string,
  ): Observable<AuthMessage> {
    const payload: ResetPasswordRequest = { email, token, newPassword, confirmPassword };
    return this.http.post<AuthMessage>(`${environment.apiUrl}/auth/reset-password`, payload);
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

  private toLoginError(error: unknown): LoginError {
    if (!(error instanceof HttpErrorResponse)) return new LoginError({}, GENERIC_ERROR);
    if (error.status === 401) return new LoginError({}, 'Credenciales incorrectas');
    const problemDetails = error.error as ProblemDetails | null;
    const code = problemDetails?.code ?? problemDetails?.extensions?.code;
    if (error.status === 403 && code === 'email_not_confirmed')
      return new LoginError({}, 'Tenés que confirmar tu email antes de entrar', code);
    if (error.status === 423)
      return new LoginError(
        {},
        'Tu cuenta está bloqueada temporalmente. Intentá iniciar sesión más tarde.',
      );
    if (error.status !== 400) return new LoginError({}, GENERIC_ERROR);

    const problem = error.error as ValidationProblemDetails | null;
    const fieldErrors: Record<string, string> = {};
    const generalErrors: string[] = [];
    for (const [key, value] of Object.entries(problem?.errors ?? {})) {
      if (!Array.isArray(value)) continue;
      const messages = value
        .filter((message): message is string => typeof message === 'string')
        .map((message) => BACKEND_MESSAGE_TRANSLATIONS[message] ?? message)
        .join(' ');
      if (!messages) continue;
      const field = key.split('.').pop()?.toLowerCase();
      if (field === 'identifier' || field === 'password') fieldErrors[field] = messages;
      else generalErrors.push(messages);
    }
    const formError =
      generalErrors.join(' ') ||
      (Object.keys(fieldErrors).length === 0 ? 'Revisá los datos ingresados.' : null);
    return new LoginError(fieldErrors, formError);
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
