import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  GuardResult,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { firstValueFrom, isObservable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { createSession } from '../../features/auth/auth-test-helpers';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { authGuard } from './auth-guard';
import { guestGuard } from './guest-guard';
import { roleGuard } from './role-guard';
import { RateLimitNoticeService } from '../../shared/services/rate-limit-notice';

describe('Functional route guards', () => {
  let router: Router;
  let storage: TokenStorageService;
  let httpMock: HttpTestingController;
  const refreshUrl = `${environment.apiUrl}/auth/refresh`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  async function runGuard(
    guard: CanActivateFn,
    url = '/library?tab=playing',
    data = {},
    queryParams = {},
  ): Promise<GuardResult> {
    const route = new ActivatedRouteSnapshot();
    route.data = data;
    route.queryParams = queryParams;
    const result = TestBed.runInInjectionContext(() =>
      guard(route, { url } as RouterStateSnapshot),
    );
    return isObservable(result) ? firstValueFrom(result) : result;
  }

  function expiredSession(role = 'User'): void {
    const session = createSession();
    localStorage.setItem(
      'playtrough.session',
      JSON.stringify({
        ...session,
        expiresAt: new Date(Date.now() - 1_000).toISOString(),
        user: { ...session.user, role },
      }),
    );
  }

  function loginReturnUrl(result: GuardResult): string {
    expect(result).toBeInstanceOf(UrlTree);
    const tree = result as UrlTree;
    expect(router.serializeUrl(tree).split('?')[0]).toBe('/auth/login');
    return tree.queryParams['returnUrl'];
  }

  it.each([authGuard, roleGuard])(
    'cancels navigation on refresh 429 without clearing credentials or redirecting',
    async (guard) => {
      expiredSession('Admin');
      const original = storage.readTokens();
      const auth = TestBed.inject(AuthService);
      const clear = vi.spyOn(auth, 'clearSession');
      const result = runGuard(guard, '/admin/roles', { minRole: 'Admin' });
      httpMock.expectOne(refreshUrl).flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '90' },
      });
      expect(await result).toBe(false);
      expect(storage.readTokens()).toEqual(original);
      expect(clear).not.toHaveBeenCalled();
      expect(router.navigateByUrl).not.toHaveBeenCalled();
      expect(TestBed.inject(RateLimitNoticeService).message()).toBe(
        'Demasiados intentos. Probá de nuevo en 1 minuto y 30 segundos',
      );
      httpMock.expectNone(refreshUrl);
    },
  );

  it('authGuard allows a restored valid session without refreshing', async () => {
    storage.save(createSession());
    expect(await runGuard(authGuard)).toBe(true);
    httpMock.expectNone(refreshUrl);
  });

  it('authGuard does not refresh a still-valid token within the warning window', async () => {
    storage.save({ ...createSession(), expiresAt: new Date(Date.now() + 20_000).toISOString() });
    expect(await runGuard(authGuard)).toBe(true);
    httpMock.expectNone(refreshUrl);
  });

  it('authGuard sends unauthenticated users to login with their internal destination', async () => {
    expect(loginReturnUrl(await runGuard(authGuard))).toBe('/library?tab=playing');
    httpMock.expectNone(refreshUrl);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each(['//evil.com', 'http://evil.com', 'javascript:alert(1)', ''])(
    'authGuard sanitizes an unsafe destination %s',
    async (url) => {
      expect(loginReturnUrl(await runGuard(authGuard, url))).toBe('/library');
    },
  );

  it('authGuard renews an expired session before allowing access', async () => {
    expiredSession();
    const result = runGuard(authGuard);
    const request = httpMock.expectOne(refreshUrl);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ ...createSession(), accessToken: 'renewed', refreshToken: 'rotated' });
    expect(await result).toBe(true);
    expect(storage.readTokens()?.refreshToken).toBe('rotated');
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it.each([400, 401])(
    'authGuard returns its own login redirect after refresh rejection %s',
    async (status) => {
      expiredSession();
      const result = runGuard(authGuard);
      httpMock.expectOne(refreshUrl).flush(null, { status, statusText: 'Error' });
      expect(loginReturnUrl(await result)).toBe('/library?tab=playing');
      expect(storage.readTokens()).toBeNull();
      expect(router.navigateByUrl).not.toHaveBeenCalled();
    },
  );

  it.each([0, 503])(
    'authGuard denies access but preserves refresh credentials after transient failure %s',
    async (status) => {
      expiredSession();
      const result = runGuard(authGuard);
      const request = httpMock.expectOne(refreshUrl);
      if (status === 0) request.error(new ProgressEvent('error'));
      else request.flush(null, { status, statusText: 'Unavailable' });
      expect(loginReturnUrl(await result)).toBe('/library?tab=playing');
      expect(storage.readTokens()?.refreshToken).toBe('refresh');
      expect(router.navigateByUrl).not.toHaveBeenCalled();
    },
  );

  it('authGuard and roleGuard share an in-flight refresh', async () => {
    expiredSession('Admin');
    const authenticated = runGuard(authGuard);
    const authorized = runGuard(roleGuard, '/admin/roles', { minRole: 'Admin' });
    httpMock
      .expectOne(refreshUrl)
      .flush({ ...createSession(), user: { ...createSession().user, role: 'Admin' } });
    expect(await authenticated).toBe(true);
    expect(await authorized).toBe(true);
    httpMock.expectNone(refreshUrl);
  });

  it.each([
    ['User', 'User', true],
    ['User', 'Moderator', false],
    ['User', 'Admin', false],
    ['Moderator', 'User', true],
    ['Moderator', 'Moderator', true],
    ['Moderator', 'Admin', false],
    ['Admin', 'User', true],
    ['Admin', 'Moderator', true],
    ['Admin', 'Admin', true],
  ])('roleGuard checks %s against minimum %s: %s', async (role, minRole, allowed) => {
    storage.save({ ...createSession(), user: { ...createSession().user, role } });
    const result = await runGuard(roleGuard, '/restricted', { minRole });
    if (allowed) expect(result).toBe(true);
    else expect(router.serializeUrl(result as UrlTree)).toBe('/access-denied');
    httpMock.expectNone(refreshUrl);
  });

  it('roleGuard sends unauthenticated users to login', async () => {
    expect(loginReturnUrl(await runGuard(roleGuard, '/admin/roles', { minRole: 'Admin' }))).toBe(
      '/admin/roles',
    );
  });

  it.each([undefined, 'Owner', 1])(
    'roleGuard denies missing or unsupported minimum role %s',
    async (minRole) => {
      storage.save({ ...createSession(), user: { ...createSession().user, role: 'Admin' } });
      expect(
        router.serializeUrl((await runGuard(roleGuard, '/restricted', { minRole })) as UrlTree),
      ).toBe('/access-denied');
    },
  );

  it('roleGuard denies an unsupported current role', async () => {
    storage.save({ ...createSession(), user: { ...createSession().user, role: 'Owner' } });
    expect(
      router.serializeUrl(
        (await runGuard(roleGuard, '/restricted', { minRole: 'User' })) as UrlTree,
      ),
    ).toBe('/access-denied');
  });

  it.each(['Admin', 'Moderator'])(
    'roleGuard evaluates the renewed role %s rather than the stored role',
    async (role) => {
      expiredSession('User');
      const result = runGuard(roleGuard, '/admin/roles', { minRole: 'Admin' });
      httpMock
        .expectOne(refreshUrl)
        .flush({ ...createSession(), user: { ...createSession().user, role } });
      const resolved = await result;
      if (role === 'Admin') expect(resolved).toBe(true);
      else expect(router.serializeUrl(resolved as UrlTree)).toBe('/access-denied');
    },
  );

  it('guestGuard allows visitors without a session', async () => {
    expect(await runGuard(guestGuard, '/auth/login')).toBe(true);
  });

  it('guestGuard allows an expired session without automatically refreshing', async () => {
    expiredSession();
    expect(await runGuard(guestGuard, '/auth/login')).toBe(true);
    httpMock.expectNone(refreshUrl);
  });

  it('guestGuard redirects an active session to a safe returnUrl', async () => {
    storage.save(createSession());
    expect(
      router.serializeUrl(
        (await runGuard(
          guestGuard,
          '/auth/login',
          {},
          { returnUrl: '/library?tab=playing' },
        )) as UrlTree,
      ),
    ).toBe('/library?tab=playing');
  });

  it('guestGuard falls back to the main route for an unsafe returnUrl', async () => {
    storage.save(createSession());
    expect(
      router.serializeUrl(
        (await runGuard(guestGuard, '/auth/login', {}, { returnUrl: '//evil.com' })) as UrlTree,
      ),
    ).toBe('/library');
  });

  it('guard refresh failure does not clear a newer login', async () => {
    expiredSession();
    const result = runGuard(authGuard);
    const oldRefresh = httpMock.expectOne(refreshUrl);
    const auth = TestBed.inject(AuthService);
    auth.login('another_user', 'password').subscribe();
    const newSession = {
      ...createSession(),
      accessToken: 'new-login',
      refreshToken: 'new-refresh',
    };
    httpMock.expectOne(`${environment.apiUrl}/auth/login`).flush(newSession);
    oldRefresh.flush(null, { status: 401, statusText: 'Unauthorized' });
    await result;
    expect(storage.read()).toEqual(newSession);
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });
});
