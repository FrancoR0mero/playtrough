import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { environment } from '../../../environments/environment';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { LayoutComponent } from '../../shared/components/layout/layout';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';

describe('Reviews routing and navigation', () => {
  let http: HttpTestingController;
  let storage: TokenStorageService;
  const url = `${environment.apiUrl}/games`;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LayoutComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
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
    }
  });
  function flushDetail(path = '4200') {
    http.expectOne(`${url}/${path}`).flush({
      id: 17,
      externalId: 4200,
      name: 'Portal 2',
      coverUrl: null,
      ratingSummary: { average: null, count: 0 },
    });
  }
  function flushReviews(sort = 'recent') {
    http
      .expectOne(`${url}/17/reviews/me`)
      .flush({ code: 'review_not_found' }, { status: 404, statusText: 'Not Found' });
    flushList(sort);
  }
  function flushList(sort: string) {
    const req = http.expectOne((request) => request.url === `${url}/17/reviews`);
    expect(req.request.params.get('sort')).toBe(sort);
    req.flush({
      items: [],
      ratingSummary: { average: null, count: 0 },
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
  }

  it('opens the local-ID route used by the detail button and loads reviews using that local ID', async () => {
    storage.save(createSession());
    const harness = await RouterTestingHarness.create('/games');
    await TestBed.inject(Router).navigate(['/games/local', 17, 'reviews'], {
      queryParams: { sort: 'top' },
    });
    harness.detectChanges();
    flushDetail('local/17');
    harness.detectChanges();
    flushReviews('top');
    harness.detectChanges();
    expect(harness.routeNativeElement!.querySelector('#reviews-title')).not.toBeNull();
    expect(harness.routeNativeElement!.textContent).toContain('Portal 2');
    expect(TestBed.inject(Router).url).toBe('/games/local/17/reviews?sort=top');
    http.expectNone(`${url}/4200`);
  });

  it('protects local-ID review links and preserves their return URL for login', async () => {
    await RouterTestingHarness.create('/games/local/17/reviews?sort=top');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(
      '/games/local/17/reviews?sort=top',
    );
    http.expectNone(() => true);
  });

  it('protects direct review links and preserves the return URL for login', async () => {
    expect(routes.find((route) => route.path === 'games/:externalId/reviews')?.canActivate).toEqual(
      [authGuard],
    );
    await RouterTestingHarness.create('/games/4200/reviews?sort=top');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(
      '/games/4200/reviews?sort=top',
    );
    http.expectNone(() => true);
  });

  it('restores direct sort, synchronizes changes in the URL and falls back to Games on Back', async () => {
    storage.save(createSession());
    const harness = await RouterTestingHarness.create('/games/4200/reviews?sort=top');
    flushDetail();
    harness.detectChanges();
    flushReviews('top');
    harness.detectChanges();
    expect(document.activeElement?.id).toBe('reviews-title');
    const select = harness.routeNativeElement!.querySelector('select')!;
    expect(select.value).toBe('top');
    select.value = 'low';
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/games/4200/reviews?sort=low');
    flushList('low');
    harness.detectChanges();
    const back = vi.spyOn(TestBed.inject(Location), 'back');
    (
      harness.routeNativeElement!.querySelector('[data-action="back"]') as HTMLButtonElement
    ).click();
    await harness.fixture.whenStable();
    expect(back).not.toHaveBeenCalled();
    expect(TestBed.inject(Router).url).toBe('/games');
  });

  it('uses browser history after navigation from inside the app, even after sorting', async () => {
    storage.save(createSession());
    const harness = await RouterTestingHarness.create('/games');
    await harness.navigateByUrl('/games/4200/reviews');
    flushDetail();
    harness.detectChanges();
    flushReviews();
    harness.detectChanges();
    const select = harness.routeNativeElement!.querySelector('select')!;
    select.value = 'top';
    select.dispatchEvent(new Event('change'));
    await harness.fixture.whenStable();
    flushList('top');
    harness.detectChanges();
    const back = vi.spyOn(TestBed.inject(Location), 'back').mockImplementation(() => undefined);
    (
      harness.routeNativeElement!.querySelector('[data-action="back"]') as HTMLButtonElement
    ).click();
    expect(back).toHaveBeenCalledOnce();
  });

  it('keeps Juegos active on mobile without adding a reviews navigation link', async () => {
    storage.save(createSession());
    const fixture = TestBed.createComponent(LayoutComponent);
    fixture.detectChanges();
    await TestBed.inject(Router).navigateByUrl('/games/4200/reviews');
    fixture.detectChanges();
    flushDetail();
    fixture.detectChanges();
    flushReviews();
    fixture.detectChanges();
    const nav = fixture.nativeElement.querySelector('nav[aria-label="Navegación móvil"]');
    expect(nav.querySelector('a[href="/games"]').getAttribute('aria-current')).toBe('page');
    expect(nav.querySelector('a[href*="/reviews"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('footer a[href="https://gamebrain.co/api"]')).not.toBeNull();
  });
});
