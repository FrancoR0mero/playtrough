import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import type { LoginResponse } from '../models/login';
import { AuthService } from './auth';
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

  it('does not persist a failed login', () => {
    const auth = TestBed.inject(AuthService);
    auth.login('ana', 'wrong').subscribe({ error: () => undefined });
    http
      .expectOne(`${environment.apiUrl}/auth/login`)
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.read()).toBeNull();
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
