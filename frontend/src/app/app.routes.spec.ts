import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../environments/environment';
import { routes } from './app.routes';
import { authGuard } from './core/guards/auth-guard';
import { guestGuard } from './core/guards/guest-guard';
import { roleGuard } from './core/guards/role-guard';
import { createSession } from './features/auth/auth-test-helpers';
import { TokenStorageService } from './features/auth/services/token-storage';
import { of } from 'rxjs';
import { LibraryService } from './features/library/services/library';
import { EMPTY_LIBRARY } from './features/library/library-test-helpers';

describe('Application route access', () => {
  let storage: TokenStorageService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    httpMock = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(LibraryService), 'list').mockReturnValue(of(EMPTY_LIBRARY));
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
    }
  });

  it('protects the private library route and keeps the wildcard last', () => {
    expect(routes.find((route) => route.path === 'library')?.canActivate).toEqual([authGuard]);
    expect(routes.at(-1)?.path).toBe('**');
  });

  it('protects profile and redirects guests to login', async () => {
    expect(routes.find((route) => route.path === 'profile')?.canActivate).toEqual([authGuard]);
    await RouterTestingHarness.create('/profile');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/profile');
  });

  it('shows the current account in profile without requesting a new backend endpoint', async () => {
    storage.save(createSession());
    const harness = await RouterTestingHarness.create('/profile');
    expect(harness.routeNativeElement?.textContent).toContain(createSession().user.email);
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toBe('Mi perfil');
    httpMock.expectNone(() => true);
  });

  it('protects role administration with both guards and Admin as the minimum role', () => {
    const route = routes.find((route) => route.path === 'admin/roles');
    expect(route?.canActivate).toEqual([authGuard, roleGuard]);
    expect(route?.data?.['minRole']).toBe('Admin');
  });

  it.each(['User', 'Moderator'])(
    'denies direct role administration navigation for %s',
    async (role) => {
      storage.save({ ...createSession(), user: { ...createSession().user, role } });
      const harness = await RouterTestingHarness.create('/admin/roles');
      expect(TestBed.inject(Router).url).toBe('/access-denied');
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
        'Acceso denegado',
      );
      httpMock.expectNone((request) => request.url.includes('/admin/users'));
    },
  );

  it('redirects an anonymous role administration visit to login with returnUrl', async () => {
    await RouterTestingHarness.create('/admin/roles');
    const router = TestBed.inject(Router);
    const tree = router.parseUrl(router.url);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(tree.queryParams['returnUrl']).toBe('/admin/roles');
  });

  it('allows an Admin to open the role administration screen', async () => {
    storage.save({ ...createSession(), user: { ...createSession().user, role: 'Admin' } });
    const navigation = RouterTestingHarness.create('/admin/roles');
    let request!: TestRequest;
    await vi.waitFor(() => {
      request = httpMock.expectOne((req) => req.url === `${environment.apiUrl}/admin/users`);
    });
    request.flush({ items: [], total: 0, page: 1, pageSize: 20 });
    const harness = await navigation;
    expect(TestBed.inject(Router).url).toBe('/admin/roles');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Roles de usuarios',
    );
  });

  it.each(['auth/login', 'auth/register', 'auth/forgot-password'])(
    'protects guest-only route %s',
    (path) => {
      expect(routes.find((route) => route.path === path)?.canActivate).toEqual([guestGuard]);
    },
  );

  it.each(['reset-password', 'confirm-email'])(
    'keeps link route %s usable without a guest guard',
    (path) => {
      expect(routes.find((route) => route.path === path)?.canActivate).toBeUndefined();
    },
  );

  it.each(['/auth/login', '/auth/register', '/auth/forgot-password'])(
    'redirects authenticated users away from %s',
    async (path) => {
      storage.save(createSession());
      const harness = await RouterTestingHarness.create(path);
      expect(TestBed.inject(Router).url).toBe('/library');
      expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
        'Tu biblioteca',
      );
    },
  );

  it('allows a guest to open password recovery', async () => {
    const harness = await RouterTestingHarness.create('/auth/forgot-password');
    expect(TestBed.inject(Router).url).toBe('/auth/forgot-password');
    expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
  });

  it.each(['/reset-password', '/confirm-email'])(
    'keeps %s accessible to an active session',
    async (path) => {
      storage.save(createSession());
      const harness = await RouterTestingHarness.create(path);
      expect(TestBed.inject(Router).url).toBe(path);
      expect(harness.routeNativeElement?.querySelector('h1')).not.toBeNull();
      expect(storage.readTokens()?.accessToken).toBe('access');
    },
  );

  it.each(['/access-denied', '/unknown/path'])(
    'renders the support page at %s with a link home',
    async (path) => {
      const harness = await RouterTestingHarness.create(path);
      const heading = harness.routeNativeElement?.querySelector('h1');
      expect(heading?.textContent).toContain(
        path === '/access-denied' ? 'Acceso denegado' : 'Esta página no existe',
      );
      expect(
        harness.routeNativeElement?.querySelector('section')?.getAttribute('aria-labelledby'),
      ).toBe(heading?.id);
      const homeLink = harness.routeNativeElement?.querySelector('a');
      expect(homeLink?.textContent).toContain('Volver al inicio');
      expect(homeLink?.getAttribute('href')).toBe('/');
      expect(TestBed.inject(Router).url).toBe(path);
    },
  );

  it.each([true, false])(
    'the support page home link works with active session %s',
    async (authenticated) => {
      if (authenticated) storage.save(createSession());
      const harness = await RouterTestingHarness.create('/access-denied');
      (harness.routeNativeElement?.querySelector('a') as HTMLAnchorElement).click();
      await harness.fixture.whenStable();
      expect(TestBed.inject(Router).url).toBe(
        authenticated ? '/library' : '/auth/login?returnUrl=%2Flibrary',
      );
    },
  );

  it.each([200, 401])(
    'navigation awaits refresh and resolves correctly after status %s',
    async (status) => {
      localStorage.setItem(
        'playtrough.session',
        JSON.stringify({
          ...createSession(),
          expiresAt: new Date(Date.now() - 1_000).toISOString(),
        }),
      );
      const harness = await RouterTestingHarness.create();
      const navigation = harness.navigateByUrl('/library?tab=playing');
      let request!: TestRequest;
      await vi.waitFor(() => {
        request = httpMock.expectOne(`${environment.apiUrl}/auth/refresh`);
      });
      request.flush(
        status === 200
          ? { ...createSession(), accessToken: 'renewed', refreshToken: 'rotated' }
          : null,
        { status, statusText: status === 200 ? 'OK' : 'Unauthorized' },
      );
      await navigation;
      const router = TestBed.inject(Router);
      if (status === 200) {
        expect(router.url).toBe('/library?tab=playing');
        expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
          'Tu biblioteca',
        );
      } else {
        const loginUrl = router.parseUrl(router.url);
        expect(loginUrl.queryParams['returnUrl']).toBe('/library?tab=playing');
        expect(loginUrl.queryParams['sessionExpired']).toBe('true');
        expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
          'Iniciar sesión',
        );
      }
      httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
    },
  );
});
