import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { createSession } from '../../features/auth/auth-test-helpers';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { RateLimitError } from '../http/rate-limit-error';
import { PUBLIC_API_ENDPOINTS } from './api-endpoints';
import { jwtInterceptor } from './jwt-interceptor';

describe('Rate limiting through the JWT interceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let auth: AuthService;
  let storage: TokenStorageService;
  let router: Router;
  const api = environment.apiUrl;
  const refreshUrl = `${api}/auth/refresh`;
  const privateUrl = `${api}/admin/users`;

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
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(auth, 'refresh');
    vi.spyOn(auth, 'logout');
    vi.spyOn(auth, 'expireSession');
    vi.spyOn(auth, 'clearSession');
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

  function assertSessionPreserved() {
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.logout).not.toHaveBeenCalled();
    expect(auth.clearSession).not.toHaveBeenCalled();
    expect(auth.expireSession).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  }

  it.each([...PUBLIC_API_ENDPOINTS, '/admin/users'])(
    'propagates a typed 429 on %s without refresh or logout',
    (path) => {
      const error = vi.fn();
      const before = storage.readTokens();
      http.post(`${api}${path}`, {}).subscribe({ error });
      httpMock.expectOne(`${api}${path}`).flush(
        { code: 'rate_limited' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: { 'Retry-After': '12' },
        },
      );
      expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(RateLimitError));
      expect((error.mock.calls[0][0] as RateLimitError).retryAfterSeconds).toBe(12);
      expect(storage.readTokens()).toEqual(before);
      expect(auth.refresh).not.toHaveBeenCalled();
      assertSessionPreserved();
    },
  );

  it.each([undefined, 'invalid', '-1', '0', '1.5'])(
    'uses 60 seconds for missing or invalid Retry-After=%s',
    (header) => {
      const error = vi.fn();
      http.get(privateUrl).subscribe({ error });
      httpMock.expectOne(privateUrl).flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: header ? { 'Retry-After': header } : {},
      });
      expect((error.mock.calls[0][0] as RateLimitError).retryAfterSeconds).toBe(60);
      expect(auth.refresh).not.toHaveBeenCalled();
      assertSessionPreserved();
    },
  );

  it('preserves the session when a refresh triggered by 401 itself receives 429', () => {
    const before = storage.readTokens();
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    httpMock.expectOne(refreshUrl).flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': '7' },
    });
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(RateLimitError));
    expect((error.mock.calls[0][0] as RateLimitError).retryAfterSeconds).toBe(7);
    expect(auth.refresh).toHaveBeenCalledTimes(1);
    expect(storage.readTokens()).toEqual(before);
    httpMock.expectNone(privateUrl);
    httpMock.expectNone(refreshUrl);
    assertSessionPreserved();
  });

  it('preserves renewed credentials when the retry after refresh receives 429', () => {
    const error = vi.fn();
    http.get(privateUrl).subscribe({ error });
    httpMock.expectOne(privateUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    const renewed = {
      ...createSession(),
      accessToken: 'renewed-access',
      refreshToken: 'renewed-refresh',
    };
    httpMock.expectOne(refreshUrl).flush(renewed);
    httpMock.expectOne(privateUrl).flush(null, { status: 429, statusText: 'Too Many Requests' });
    expect(error).toHaveBeenCalledExactlyOnceWith(expect.any(RateLimitError));
    expect(storage.readTokens()?.refreshToken).toBe('renewed-refresh');
    expect(auth.refresh).toHaveBeenCalledTimes(1);
    assertSessionPreserved();
  });
});
