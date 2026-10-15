import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import type { LoginResponse } from '../models/login';
import { AuthService, LoginError } from './auth';
import { TokenStorageService } from './token-storage';

describe('AuthService login and session', () => {
  let http: HttpTestingController;
  let storage: TokenStorageService;
  const session = (): LoginResponse => ({
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  });

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
  });
  afterEach(() => {
    http.verify();
    TestBed.inject(AuthService).logout();
    vi.restoreAllMocks();
    vi.useRealTimers();
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

  it('starts unauthenticated when no session is stored', () => {
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(storage.read()).toBeNull();
  });

  it('discards an already expired session before restoring authentication at startup', () => {
    storage.save(session());
    // Advance before constructing AuthService: this tests startup, not its expiry timer.
    vi.advanceTimersByTime(900_000);
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(localStorage.getItem('playtrough.session')).toBeNull();
  });

  it('restores the stored session when the service is created', () => {
    const value = session();
    storage.save(value);
    const auth = TestBed.inject(AuthService);
    expect(auth.currentUser()).toEqual(value.user);
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('expires an active session and clears persisted tokens', () => {
    storage.save(session());
    const auth = TestBed.inject(AuthService);
    vi.advanceTimersByTime(900_000);
    expect(auth.currentUser()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
  });

  it('clears user and tokens on logout', () => {
    storage.save(session());
    const auth = TestBed.inject(AuthService);
    auth.logout();
    expect(auth.currentUser()).toBeNull();
    expect(storage.read()).toBeNull();
  });
});
