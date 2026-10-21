import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { createSession as session } from '../auth-test-helpers';
import { AuthService, LoginError } from './auth';
import { TokenStorageService } from './token-storage';

describe('AuthService login and session', () => {
  let http: HttpTestingController;
  let storage: TokenStorageService;

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  it('posts a typed login, trims identifier only, persists it and updates reactive state', () => {
    const auth = TestBed.inject(AuthService);
    const value = session();
    auth
      .login('  ana@example.com  ', ' password ')
      .subscribe((response) => expect(response).toEqual(value));
    const req = http.expectOne(`${environment.apiUrl}/auth/login`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ identifier: 'ana@example.com', password: ' password ' });
    req.flush(value);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.currentUser()).toEqual(value.user);
    expect(storage.read()).toEqual(value);
  });

  it.each([
    { status: 401, message: 'Credenciales incorrectas' },
    {
      status: 423,
      message: 'Tu cuenta está bloqueada temporalmente. Intentá iniciar sesión más tarde.',
    },
  ])(
    'propagates a typed $status error without persisting or authenticating',
    ({ status, message }) => {
      const auth = TestBed.inject(AuthService);
      const save = vi.spyOn(storage, 'save');
      const next = vi.fn();
      const error = vi.fn();
      auth.login('ana', 'wrong').subscribe({ next, error });
      http
        .expectOne(`${environment.apiUrl}/auth/login`)
        .flush(
          { title: 'Backend detail', detail: 'Internal information' },
          { status, statusText: status === 401 ? 'Unauthorized' : 'Locked' },
        );

      expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(LoginError));
      const received = error.mock.calls[0][0] as LoginError;
      expect(received.formError).toBe(message);
      expect(received.fieldErrors).toEqual({});
      expect(next).not.toHaveBeenCalled();
      expect(save).not.toHaveBeenCalled();
      expect(auth.isAuthenticated()).toBe(false);
      expect(auth.currentUser()).toBeNull();
      expect(localStorage.getItem('playtrough.session')).toBeNull();
    },
  );

  it.each([0, 500])(
    'propagates a generic login error for status %s without saving a session',
    (status) => {
      // Arrange
      const auth = TestBed.inject(AuthService);
      const error = vi.fn();
      const save = vi.spyOn(storage, 'save');

      // Act
      auth.login('ana', 'password').subscribe({ error });
      const request = http.expectOne(`${environment.apiUrl}/auth/login`);
      if (status === 0) request.error(new ProgressEvent('error'));
      else
        request.flush({ detail: 'Internal server information' }, { status, statusText: 'Error' });

      // Assert
      expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(LoginError));
      expect((error.mock.calls[0][0] as LoginError).formError).toBe(
        'Ocurrió un error inesperado. Inténtalo de nuevo.',
      );
      expect(save).not.toHaveBeenCalled();
      expectClearedSession(auth);
    },
  );

  it('maps a 400 validation response to typed field errors without saving a session', () => {
    // Arrange
    const auth = TestBed.inject(AuthService);
    const error = vi.fn();

    // Act
    auth.login('ana', 'password').subscribe({ error });
    http.expectOne(`${environment.apiUrl}/auth/login`).flush(
      {
        errors: {
          Identifier: ['Email or username is required.'],
          Password: ['Mensaje específico del servidor.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(LoginError));
    const received = error.mock.calls[0][0] as LoginError;
    expect(received.fieldErrors).toEqual({
      identifier: 'El email o nombre de usuario es obligatorio.',
      password: 'Mensaje específico del servidor.',
    });
    expect(received.formError).toBeNull();
    expectClearedSession(auth);
  });

  it('preserves 400 validation messages that have no recognized field', () => {
    // Arrange
    const auth = TestBed.inject(AuthService);
    const error = vi.fn();

    // Act
    auth.login('ana', 'password').subscribe({ error });
    http
      .expectOne(`${environment.apiUrl}/auth/login`)
      .flush(
        { errors: { '': ['Solicitud no válida.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(LoginError));
    const received = error.mock.calls[0][0] as LoginError;
    expect(received.fieldErrors).toEqual({});
    expect(received.formError).toBe('Solicitud no válida.');
    expectClearedSession(auth);
  });

  it('starts unauthenticated when no session is stored', () => {
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(storage.read()).toBeNull();
  });

  it('does not authenticate an expired access token but preserves refresh credentials at startup', () => {
    storage.save(session());
    // Advance before constructing AuthService: this tests startup, not its expiry timer.
    vi.advanceTimersByTime(900_000);
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(storage.readTokens()).toEqual({ accessToken: 'access', refreshToken: 'refresh' });
  });

  it('restores the stored session when the service is created', () => {
    const value = session();
    storage.save(value);
    const auth = TestBed.inject(AuthService);
    expect(auth.currentUser()).toEqual(value.user);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('expires active authentication while retaining tokens for an explicit refresh', () => {
    storage.save(session());
    const auth = TestBed.inject(AuthService);
    vi.advanceTimersByTime(900_000);
    expect(auth.currentUser()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
    expect(storage.readTokens()).toEqual({ accessToken: 'access', refreshToken: 'refresh' });
  });

  function authenticatedService(): AuthService {
    storage.save(session());
    return TestBed.inject(AuthService);
  }

  function expectClearedSession(auth: AuthService): void {
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(storage.read()).toBeNull();
    expect(storage.readTokens()).toBeNull();
    expect(localStorage.getItem('playtrough.session')).toBeNull();
  }

  it('posts logout with the refresh token, delegates bearer headers to the interceptor and clears on 204', () => {
    // Arrange
    const auth = authenticatedService();
    const complete = vi.fn();
    const error = vi.fn();

    // Act
    auth.logout().subscribe({ complete, error });
    const request = http.expectOne(`${environment.apiUrl}/auth/logout`);

    // Assert the outgoing contract and pending state before resolving the request.
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    expect(complete).not.toHaveBeenCalled();
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(complete).toHaveBeenCalledOnce();
    expect(error).not.toHaveBeenCalled();
    expectClearedSession(auth);
    auth.restoreSession();
    expectClearedSession(auth);
  });

  it.each([0, 400, 401, 500, 503])(
    'suppresses logout error %s and always clears storage and reactive state',
    (status) => {
      // Arrange
      const auth = authenticatedService();
      const error = vi.fn();
      const complete = vi.fn();

      // Act
      auth.logout().subscribe({ error, complete });
      const request = http.expectOne(`${environment.apiUrl}/auth/logout`);
      if (status === 0) request.error(new ProgressEvent('error'));
      else request.flush({ title: 'Logout failed' }, { status, statusText: 'Error' });

      // Assert
      expect(error).not.toHaveBeenCalled();
      expect(complete).toHaveBeenCalledOnce();
      expectClearedSession(auth);
    },
  );

  it.each(['absent-session', 'missing-refresh-token'])(
    'clears local authentication without an HTTP request for %s',
    (stored) => {
      // Arrange: keep an authenticated in-memory user while removing the stored refresh token.
      const auth = authenticatedService();
      if (stored === 'absent-session') storage.clear();
      else
        localStorage.setItem(
          'playtrough.session',
          JSON.stringify({ ...session(), refreshToken: '' }),
        );
      const complete = vi.fn();

      // Act
      auth.logout().subscribe({ complete });

      // Assert
      http.expectNone(`${environment.apiUrl}/auth/logout`);
      expect(complete).toHaveBeenCalledOnce();
      expectClearedSession(auth);
    },
  );

  it('clears the session after five seconds when the logout API does not respond', () => {
    // Arrange
    const auth = authenticatedService();
    const complete = vi.fn();
    const error = vi.fn();
    auth.logout().subscribe({ complete, error });
    const request = http.expectOne(`${environment.apiUrl}/auth/logout`);

    // Act
    vi.advanceTimersByTime(5_000);

    // Assert
    expect(request.cancelled).toBe(true);
    expect(complete).toHaveBeenCalledOnce();
    expect(error).not.toHaveBeenCalled();
    expectClearedSession(auth);
  });
});
