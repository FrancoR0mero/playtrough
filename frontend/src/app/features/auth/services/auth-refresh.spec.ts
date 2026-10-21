import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { createSession } from '../auth-test-helpers';
import type { LoginResponse } from '../models/login';
import { AuthService } from './auth';
import { TokenStorageService } from './token-storage';

describe('AuthService refresh', () => {
  let httpMock: HttpTestingController;
  let storage: TokenStorageService;
  let router: Router;
  const refreshUrl = `${environment.apiUrl}/auth/refresh`;
  const expiredLogin = '/auth/login?returnUrl=%2Flibrary&sessionExpired=true';

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/library');
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  function authenticated(): AuthService {
    storage.save(createSession());
    return TestBed.inject(AuthService);
  }

  function replacement(suffix = 'new'): LoginResponse {
    return {
      ...createSession(),
      accessToken: `access-${suffix}`,
      refreshToken: `refresh-${suffix}`,
      user: { id: 1, username: 'ana_updated', email: 'new@example.com', role: 'Admin' },
    };
  }

  function expectCleared(auth: AuthService): void {
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.currentUser()).toBeNull();
    expect(auth.getAccessToken()).toBeNull();
    expect(storage.read(true)).toBeNull();
    expect(storage.readTokens()).toBeNull();
    expect(localStorage.getItem('playtrough.session')).toBeNull();
  }

  it('posts the stored refresh token without Authorization and replaces tokens and user on success', () => {
    // Arrange
    const auth = authenticated();
    const next = vi.fn();
    const renewed = replacement();

    // Act
    auth.refresh().subscribe(next);
    const request = httpMock.expectOne(refreshUrl);

    // Assert request and response contract.
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(renewed);
    expect(next).toHaveBeenCalledExactlyOnceWith(renewed.accessToken);
    expect(storage.read()).toEqual(renewed);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.currentUser()).toEqual(renewed.user);
    expect(auth.getAccessToken()).toBe(renewed.accessToken);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each([400, 401])('clears the session and redirects on refresh status %s', (status) => {
    // Arrange
    const auth = authenticated();
    const error = vi.fn();

    // Act
    auth.refresh().subscribe({ error });
    httpMock
      .expectOne(refreshUrl)
      .flush({ title: 'Refresh failed' }, { status, statusText: 'Error' });

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect((error.mock.calls[0][0] as HttpErrorResponse).status).toBe(status);
    expectCleared(auth);
    expect(router.navigateByUrl).toHaveBeenCalledExactlyOnceWith(expiredLogin, {
      replaceUrl: true,
    });
  });

  it('propagates network errors while retaining stored tokens, user and authentication', () => {
    // Arrange
    const auth = authenticated();
    const original = storage.read();
    const clear = vi.spyOn(storage, 'clear');
    const error = vi.fn();

    // Act
    auth.refresh().subscribe({ error });
    httpMock.expectOne(refreshUrl).error(new ProgressEvent('error'));

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect((error.mock.calls[0][0] as HttpErrorResponse).status).toBe(0);
    expect(clear).not.toHaveBeenCalled();
    expect(storage.read()).toEqual(original);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.currentUser()).toEqual(original!.user);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each(['absent-session', 'missing-refresh-token'])(
    'clears authentication without making a request when %s is stored',
    (stored) => {
      // Arrange
      const auth = authenticated();
      if (stored === 'absent-session') storage.clear();
      else
        localStorage.setItem(
          'playtrough.session',
          JSON.stringify({ ...createSession(), refreshToken: '' }),
        );
      const error = vi.fn();

      // Act
      auth.refresh().subscribe({ error });

      // Assert
      httpMock.expectNone(refreshUrl);
      expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(Error));
      expectCleared(auth);
      expect(router.navigateByUrl).toHaveBeenCalledExactlyOnceWith(expiredLogin, {
        replaceUrl: true,
      });
    },
  );

  it('shares one HTTP refresh between three concurrent callers', () => {
    // Arrange
    const auth = authenticated();
    const receivers = [vi.fn(), vi.fn(), vi.fn()];

    // Act
    receivers.forEach((next) => auth.refresh().subscribe(next));
    const request = httpMock.expectOne(refreshUrl);
    request.flush(replacement());

    // Assert
    receivers.forEach((next) => expect(next).toHaveBeenCalledExactlyOnceWith('access-new'));
    expect(storage.readTokens()?.refreshToken).toBe('refresh-new');
  });

  it('makes a new request with the rotated token after the prior refresh completes', () => {
    // Arrange
    const auth = authenticated();
    const first = vi.fn();
    const second = vi.fn();
    auth.refresh().subscribe(first);
    httpMock.expectOne(refreshUrl).flush(replacement('first'));

    // Act
    auth.refresh().subscribe(second);
    const request = httpMock.expectOne(refreshUrl);

    // Assert
    expect(request.request.body).toEqual({ refreshToken: 'refresh-first' });
    request.flush(replacement('second'));
    expect(first).toHaveBeenCalledExactlyOnceWith('access-first');
    expect(second).toHaveBeenCalledExactlyOnceWith('access-second');
  });

  it('does not cache a completed result when a caller reuses the returned Observable', () => {
    // Arrange
    const auth = authenticated();
    const refresh = auth.refresh();
    refresh.subscribe();
    httpMock.expectOne(refreshUrl).flush(replacement('first'));
    const next = vi.fn();

    // Act
    refresh.subscribe(next);
    const request = httpMock.expectOne(refreshUrl);
    request.flush(replacement('second'));

    // Assert
    expect(next).toHaveBeenCalledExactlyOnceWith('access-second');
  });

  it('releases the shared request before notifying completion callbacks', () => {
    // Arrange
    const auth = authenticated();
    const next = vi.fn();
    auth.refresh().subscribe({ complete: () => auth.refresh().subscribe(next) });

    // Act
    httpMock.expectOne(refreshUrl).flush(replacement('first'));
    const secondRequest = httpMock.expectOne(refreshUrl);

    // Assert
    expect(secondRequest.request.body).toEqual({ refreshToken: 'refresh-first' });
    secondRequest.flush(replacement('second'));
    expect(next).toHaveBeenCalledExactlyOnceWith('access-second');
  });

  it('allows an immediate retry from a network error callback without sharing the failed request', () => {
    // Arrange
    const auth = authenticated();
    const next = vi.fn();
    auth.refresh().subscribe({ error: () => auth.refresh().subscribe(next) });

    // Act
    httpMock.expectOne(refreshUrl).error(new ProgressEvent('error'));
    const retry = httpMock.expectOne(refreshUrl);

    // Assert
    expect(retry.request.body).toEqual({ refreshToken: 'refresh' });
    retry.flush(replacement());
    expect(next).toHaveBeenCalledExactlyOnceWith('access-new');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('keeps the shared request alive when one caller unsubscribes', () => {
    // Arrange
    const auth = authenticated();
    const next = vi.fn();
    const first = auth.refresh().subscribe();
    auth.refresh().subscribe(next);
    const request = httpMock.expectOne(refreshUrl);

    // Act
    first.unsubscribe();

    // Assert
    expect(request.cancelled).toBe(false);
    request.flush(replacement());
    expect(next).toHaveBeenCalledExactlyOnceWith('access-new');
  });

  it('renews an expired access session after service startup without automatically refreshing', () => {
    // Arrange
    storage.save(createSession());
    vi.advanceTimersByTime(900_000);
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.isAccessTokenExpired(0)).toBe(true);
    httpMock.expectNone(refreshUrl);

    // Act
    auth.refresh().subscribe();
    httpMock.expectOne(refreshUrl).flush(replacement());

    // Assert
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.getAccessToken()).toBe('access-new');
    expect(auth.isAccessTokenExpired()).toBe(false);
  });

  it('keeps refresh credentials if a network error occurs after access expiry, allowing a later retry', () => {
    // Arrange
    const auth = authenticated();
    auth.refresh().subscribe({ error: vi.fn() });
    const request = httpMock.expectOne(refreshUrl);

    // Act
    vi.advanceTimersByTime(900_000);
    request.error(new ProgressEvent('error'));

    // Assert
    expect(storage.readTokens()?.refreshToken).toBe('refresh');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    auth.refresh().subscribe();
    httpMock.expectOne(refreshUrl).flush(replacement());
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('does not restore authentication when an in-flight refresh returns after logout', () => {
    // Arrange
    const auth = authenticated();
    const error = vi.fn();
    auth.refresh().subscribe({ error });
    const request = httpMock.expectOne(refreshUrl);
    auth.logout().subscribe();
    httpMock
      .expectOne(`${environment.apiUrl}/auth/logout`)
      .flush(null, { status: 204, statusText: 'No Content' });

    // Act
    request.flush(replacement());

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(Error));
    expectCleared(auth);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('does not overwrite or clear a newer login when an old refresh finishes', () => {
    // Arrange
    const auth = authenticated();
    const error = vi.fn();
    auth.refresh().subscribe({ error });
    const request = httpMock.expectOne(refreshUrl);
    const newerSession = replacement('new-login');
    auth.login('other_user', 'password').subscribe();
    httpMock.expectOne(`${environment.apiUrl}/auth/login`).flush(newerSession);

    // Act
    request.flush(replacement('old-refresh'));

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(Error));
    expect(storage.read()).toEqual(newerSession);
    expect(auth.currentUser()).toEqual(newerSession.user);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('clears the session when a malformed success response cannot be stored', () => {
    // Arrange
    const auth = authenticated();
    const error = vi.fn();

    // Act
    auth.refresh().subscribe({ error });
    httpMock.expectOne(refreshUrl).flush({ accessToken: 'missing-refresh-and-user' });

    // Assert
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(Error));
    expectCleared(auth);
    expect(router.navigateByUrl).toHaveBeenCalledWith(expiredLogin, { replaceUrl: true });
  });

  it.each([500, 503])('retains the session on refresh status %s', (status) => {
    const auth = authenticated();
    const original = storage.read();
    const error = vi.fn();

    auth.refresh().subscribe({ error });
    httpMock.expectOne(refreshUrl).flush(null, { status, statusText: 'Unavailable' });

    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(storage.read()).toEqual(original);
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('returns no access token and reports expiry when there is no stored session', () => {
    // Arrange
    const auth = TestBed.inject(AuthService);

    // Act / Assert
    expect(auth.getAccessToken()).toBeNull();
    expect(auth.isAccessTokenExpired()).toBe(true);
  });

  it.each([
    { remainingMs: 31_000, margin: undefined, expired: false },
    { remainingMs: 30_000, margin: undefined, expired: true },
    { remainingMs: 1, margin: 0, expired: false },
    { remainingMs: 0, margin: 0, expired: true },
    { remainingMs: -1, margin: 0, expired: true },
    { remainingMs: 60_000, margin: 60, expired: true },
    { remainingMs: 60_001, margin: 60, expired: false },
    { remainingMs: 1, margin: -10, expired: false },
  ])(
    'detects expiry for remaining=$remainingMs ms and margin=$margin seconds',
    ({ remainingMs, margin, expired }) => {
      // Arrange
      localStorage.setItem(
        'playtrough.session',
        JSON.stringify({
          ...createSession(),
          expiresAt: new Date(Date.now() + remainingMs).toISOString(),
        }),
      );
      const auth = TestBed.inject(AuthService);

      // Act / Assert
      expect(auth.getAccessToken()).toBe('access');
      expect(auth.isAccessTokenExpired(margin)).toBe(expired);
    },
  );
});
