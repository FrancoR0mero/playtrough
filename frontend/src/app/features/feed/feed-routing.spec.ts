import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { environment } from '../../../environments/environment';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { createFriendsStateMock } from '../friends/friends-test-helpers';
import { FriendsState } from '../friends/services/friends-state';
import { NAVIGATION_ITEMS } from '../../shared/components/layout/layout-navigation';

describe('Feed routing', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: FriendsState, useValue: createFriendsStateMock() },
      ],
    });
    TestBed.inject(TokenStorageService).clear();
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.inject(TokenStorageService).clear();
  });
  it('protects the feed and preserves the default library route', async () => {
    expect(routes.find((route) => route.path === 'feed')?.canActivate).toEqual([authGuard]);
    expect(routes.find((route) => route.path === '')?.redirectTo).toBe('library');
    await RouterTestingHarness.create('/feed');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/feed');
    TestBed.inject(HttpTestingController).expectNone(`${environment.apiUrl}/feed`);
  });
  it.each(['User', 'Moderator', 'Admin'])('loads the feed for %s', async (role) => {
    const session = createSession();
    TestBed.inject(TokenStorageService).save({ ...session, user: { ...session.user, role } });
    const harness = await RouterTestingHarness.create('/feed');
    const req = TestBed.inject(HttpTestingController).expectOne(
      `${environment.apiUrl}/feed?limit=20`,
    );
    req.flush({ items: [], nextCursor: null, hasMore: false });
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Inicio');
    expect(NAVIGATION_ITEMS[0]).toMatchObject({
      label: 'Inicio',
      path: '/feed',
      minRole: 'User',
      enabled: true,
    });
  });
});
