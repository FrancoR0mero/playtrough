import {
  HttpClient,
  HttpErrorResponse,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { createSession } from '../../features/auth/auth-test-helpers';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { jwtInterceptor } from './jwt-interceptor';

describe('JWT interceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let storage: TokenStorageService;
  let router: Router;
  const api = environment.apiUrl;
  const refreshUrl = `${api}/auth/refresh`;
  const privateUrl = `${api}/admin/users`;
  const expiredLogin = '/auth/login?returnUrl=%2Flibrary%3Ftab%3Dplaying&sessionExpired=true';

  beforeEach(() => {
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    storage.save(createSession());
    router = TestBed.inject(Router);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/library?tab=playing');
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
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

  function refreshSuccess(): void {
    const request = httpMock.expectOne(refreshUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ refreshToken: 'refresh' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ ...createSession(), accessToken: 'access-new', refreshToken: 'refresh-new' });
  }

  it('attaches the stored token to protected API requests', () => {
    http.get(privateUrl).subscribe();
    const request = httpMock.expectOne(privateUrl);
    expect(request.request.headers.get('Authorization')).toBe('Bearer access');
    request.flush([]);
  });

  it.each([
    '/auth/login',
    '/auth/register',
    '/auth/refresh',
    '/auth/forgot-password',
    '/auth/reset-password',
    '/auth/confirm-email',
    '/auth/resend-confirmation',
  ])('does not attach a token or refresh on 401 from public endpoint %s', (path) => {
    const error = vi.fn();
    http.post(`${api}${path}`, {}).subscribe({ error });
    const request = httpMock.expectOne(`${api}${path}`);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectNone(refreshUrl);
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each([
    'https://external.example/api/users',
    `${api}-external/users`,
    `${new URL(api).origin}/elsewhere`,
    '//external.example/api/users',
  ])('does not attach tokens or handle external 401s: %s', (url) => {
    http.get(url).subscribe({ error: vi.fn() });
    const request = httpMock.expectOne(url);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectNone(refreshUrl);
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('recognizes public endpoints with query strings, trailing slash and mixed case', () => {
    const url = `${api}/Auth/Confirm-Email/?email=ana%40example.com`;
    http.post(url, {}).subscribe();
    const request = httpMock.expectOne(url);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('does not treat a longer protected path as a public endpoint', () => {
    const url = `${api}/auth/login/history`;
    http.get(url).subscribe();
    const request = httpMock.expectOne(url);
    expect(request.request.headers.get('Authorization')).toBe('Bearer access');
    request.flush([]);
  });

  it('refreshes once and retries the original request with its body, query and new token', () => {
    const result = vi.fn();
    const url = `${api}/admin/users/2/role?source=test`;
    const body = { role: 'Moderator' };
    http.put(url, body).subscribe(result);
    httpMock.expectOne(url).flush(null, { status: 401, statusText: 'Unauthorized' });
    refreshSuccess();
    const retry = httpMock.expectOne(url);
    expect(retry.request.method).toBe('PUT');
    expect(retry.request.body).toEqual(body);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-new');
    retry.flush({ id: 2, role: 'Moderator' });
    expect(result).toHaveBeenCalledExactlyOnceWith({ id: 2, role: 'Moderator' });
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('shares one refresh between three simultaneous protected 401s', () => {
    const results = [vi.fn(), vi.fn(), vi.fn()];
    results.forEach((next, i) => http.get(`${privateUrl}?page=${i + 1}`).subscribe(next));
    const originals = httpMock.match((request) => request.url.startsWith(privateUrl));
    expect(originals).toHaveLength(3);
    originals.forEach((request) =>
      request.flush(null, { status: 401, statusText: 'Unauthorized' }),
    );
    refreshSuccess();
    const retries = httpMock.match((request) => request.url.startsWith(privateUrl));
    expect(retries).toHaveLength(3);
    retries.forEach((request, i) => {
      expect(request.request.headers.get('Authorization')).toBe('Bearer access-new');
      request.flush({ page: i + 1 });
    });
    results.forEach((next, i) => expect(next).toHaveBeenCalledExactlyOnceWith({ page: i + 1 }));
    httpMock.expectNone(refreshUrl);
  });

  it('reuses the renewed token for a late 401 without rotating the refresh token again', () => {
    http.get(`${privateUrl}?page=1`).subscribe();
    http.get(`${privateUrl}?page=2`).subscribe();
    const first = httpMock.expectOne(`${privateUrl}?page=1`);
    const late = httpMock.expectOne(`${privateUrl}?page=2`);
    first.flush(null, { status: 401, statusText: 'Unauthorized' });
    refreshSuccess();
    httpMock.expectOne(`${privateUrl}?page=1`).flush([]);
    late.flush(null, { status: 401, statusText: 'Unauthorized' });
    const retry = httpMock.expectOne(`${privateUrl}?page=2`);
    expect(retry.request.headers.get('Authorization')).toBe('Bearer access-new');
    retry.flush([]);
    httpMock.expectNone(refreshUrl);
  });

  it('propagates a persistent 401 after a single retry and expires the session', () => {
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    refreshSuccess();
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectNone(refreshUrl);
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(storage.readTokens()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledExactlyOnceWith(expiredLogin, {
      replaceUrl: true,
    });
  });

  it.each([400, 401])(
    'expires the session and redirects once when refresh returns %s',
    (status) => {
      const errors = [vi.fn(), vi.fn(), vi.fn()];
      errors.forEach((error) => http.get(privateUrl).subscribe({ error }));
      httpMock
        .match(privateUrl)
        .forEach((request) => request.flush(null, { status: 401, statusText: 'Unauthorized' }));
      httpMock.expectOne(refreshUrl).flush(null, { status, statusText: 'Error' });
      expect(storage.readTokens()).toBeNull();
      expect(auth.isAuthenticated()).toBe(false);
      expect(router.navigateByUrl).toHaveBeenCalledExactlyOnceWith(expiredLogin, {
        replaceUrl: true,
      });
      errors.forEach((error) =>
        expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse)),
      );
      httpMock.expectNone(privateUrl);
    },
  );

  it.each([403, 500, 503, 0])('preserves the session on protected request error %s', (status) => {
    const error = vi.fn();
    const original = storage.read();
    http.get(privateUrl).subscribe({ error });
    const request = httpMock.expectOne(privateUrl);
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush(null, { status, statusText: 'Error' });
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    httpMock.expectNone(refreshUrl);
    expect(storage.read()).toEqual(original);
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each([500, 503, 0])('preserves the session when refresh has transient error %s', (status) => {
    const original = storage.read();
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    const refresh = httpMock.expectOne(refreshUrl);
    if (status === 0) refresh.error(new ProgressEvent('error'));
    else refresh.flush(null, { status, statusText: 'Error' });
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(storage.read()).toEqual(original);
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    httpMock.expectNone(privateUrl);
  });

  it.each([403, 500, 0])('preserves the renewed session on retry error %s', (status) => {
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    refreshSuccess();
    const retry = httpMock.expectOne(privateUrl);
    if (status === 0) retry.error(new ProgressEvent('error'));
    else retry.flush(null, { status, statusText: 'Error' });
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(storage.readTokens()?.accessToken).toBe('access-new');
    expect(auth.isAuthenticated()).toBe(true);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    httpMock.expectNone(refreshUrl);
  });

  it('does not attempt refresh without stored refresh credentials', () => {
    storage.clear();
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectNone(refreshUrl);
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(HttpErrorResponse));
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.navigateByUrl).toHaveBeenCalledExactlyOnceWith(expiredLogin, {
      replaceUrl: true,
    });
  });

  it.each([
    '/auth/login',
    '/auth/register',
    '/auth/forgot-password',
    '/reset-password?token=x',
    '/confirm-email?token=x',
  ])('does not redirect again from auth page %s when refresh is rejected', (url) => {
    vi.spyOn(router, 'url', 'get').mockReturnValue(url);
    http.get(privateUrl).subscribe({ error: vi.fn() });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(refreshUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(auth.isAuthenticated()).toBe(false);
    expect(storage.readTokens()).toBeNull();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
