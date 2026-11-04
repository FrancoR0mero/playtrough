import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { LayoutComponent } from '../../shared/components/layout/layout';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { of } from 'rxjs';
import { LibraryService } from '../library/services/library';
import { EMPTY_LIBRARY } from '../library/library-test-helpers';

describe('Game catalog routing and layout', () => {
  let storage: TokenStorageService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LayoutComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(TestBed.inject(LibraryService), 'list').mockReturnValue(of(EMPTY_LIBRARY));
  });

  afterEach(() => {
    try {
      http.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
    }
  });

  async function flushSearch(q: string): Promise<void> {
    let request!: TestRequest;
    await vi.waitFor(() => {
      request = http.expectOne((req) => req.url === `${environment.apiUrl}/games/search`);
    });
    expect(request.request.params.get('q')).toBe(q);
    request.flush({ items: [], page: 1, pageSize: 20, hasMore: false });
  }

  it('protects /games and redirects anonymous users to login preserving the search', async () => {
    expect(routes.find((route) => route.path === 'games')?.canActivate).toEqual([authGuard]);
    await RouterTestingHarness.create('/games?q=Portal');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe('/games?q=Portal');
    http.expectNone((req) => req.url.includes('/games'));
  });

  it('restores a direct link and keeps the real URL synchronized with typing and clearing', async () => {
    storage.save(createSession());
    const navigation = RouterTestingHarness.create('/games?q=Portal');
    await flushSearch('Portal');
    const harness = await navigation;
    const input = harness.routeNativeElement!.querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('Portal');
    input.value = 'Hades';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await flushSearch('Hades');
    await harness.fixture.whenStable();
    const router = TestBed.inject(Router);
    expect(router.parseUrl(router.url).queryParams['q']).toBe('Hades');
    input.value = '';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await harness.fixture.whenStable();
    expect(router.url).toBe('/games');
    http.expectNone((req) => req.url.includes('/games/search'));
  });

  it.each(['User', 'Moderator', 'Admin'])(
    'shows Juegos and RAWG attribution on the search page for %s',
    async (role) => {
      storage.save({ ...createSession(), user: { ...createSession().user, role } });
      const fixture = TestBed.createComponent(LayoutComponent);
      fixture.detectChanges();
      await TestBed.inject(Router).navigateByUrl('/games');
      await fixture.whenStable();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain('Juegos');
      const nav = fixture.nativeElement.querySelector('nav a[href="/games"]') as HTMLAnchorElement;
      expect(nav.textContent?.trim()).toBe('Juegos');
      expect(nav.getAttribute('aria-current')).toBe('page');
      const credit = fixture.nativeElement.querySelector(
        'footer a[href="https://rawg.io"]',
      ) as HTMLAnchorElement;
      expect(credit).not.toBeNull();
      expect(credit.getAttribute('rel')).toBe('noopener noreferrer');
      expect(credit.getAttribute('target')).toBe('_blank');
    },
  );

  it('keeps RAWG attribution when navigating between authenticated screens', async () => {
    storage.save(createSession());
    const fixture = TestBed.createComponent(LayoutComponent);
    fixture.detectChanges();
    const router = TestBed.inject(Router);
    for (const path of ['/games', '/library']) {
      await router.navigateByUrl(path);
      await fixture.whenStable();
      fixture.detectChanges();
      expect(
        fixture.nativeElement.querySelectorAll('footer a[href="https://rawg.io"]'),
      ).toHaveLength(1);
    }
  });

  it('focuses the catalog search when adding a game from the empty library', async () => {
    storage.save(createSession());
    const harness = await RouterTestingHarness.create('/library');
    const link = harness.routeNativeElement!.querySelector(
      '[data-library-empty] a',
    ) as HTMLAnchorElement;
    expect(link.textContent).toContain('Agregar juego');
    link.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/games?focus=search');
    expect(document.activeElement?.id).toBe('game-search');
    http.expectNone((req) => req.url.includes('/games/search'));
  });

  it('hides the game navigation without a session', () => {
    const fixture = TestBed.createComponent(LayoutComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav a[href="/games"]')).toBeNull();
  });
});
