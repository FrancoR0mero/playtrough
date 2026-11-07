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
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { EMPTY_LIBRARY } from './library-test-helpers';

describe('Library route and query persistence', () => {
  let http: HttpTestingController;
  let storage: TokenStorageService;
  beforeEach(() => {
    TestBed.configureTestingModule({
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
    }
  });
  async function flush(expected: Record<string, string>): Promise<void> {
    let request!: TestRequest;
    await vi.waitFor(() => {
      request = http.expectOne((req) => req.url === `${environment.apiUrl}/library`);
    });
    for (const [key, value] of Object.entries(expected))
      expect(request.request.params.get(key)).toBe(value);
    request.flush(EMPTY_LIBRARY);
  }

  it('protects the private route and redirects guests preserving the filters', async () => {
    expect(routes.find((route) => route.path === 'library')?.canActivate).toEqual([authGuard]);
    await RouterTestingHarness.create('/library?status=playing&q=Portal&sort=name');
    const router = TestBed.inject(Router);
    expect(router.url.split('?')[0]).toBe('/auth/login');
    expect(router.parseUrl(router.url).queryParams['returnUrl']).toBe(
      '/library?status=playing&q=Portal&sort=name',
    );
    http.expectNone((req) => req.url.includes('/library'));
  });

  it('restores a direct link and keeps tabs, search, sort and back navigation synchronized', async () => {
    storage.save(createSession());
    const navigation = RouterTestingHarness.create('/library?status=playing&q=Portal&sort=name');
    await flush({ status: 'playing', q: 'Portal', sort: 'name' });
    const harness = await navigation;
    const router = TestBed.inject(Router);
    expect(
      (harness.routeNativeElement!.querySelector('#library-search') as HTMLInputElement).value,
    ).toBe('Portal');
    (
      harness.routeNativeElement!.querySelector('[data-tab="backlog"]') as HTMLButtonElement
    ).click();
    await flush({ status: 'backlog', q: 'Portal', sort: 'name' });
    await harness.fixture.whenStable();
    expect(router.parseUrl(router.url).queryParams['status']).toBe('backlog');
    const search = harness.routeNativeElement!.querySelector('#library-search') as HTMLInputElement;
    search.value = 'Hades';
    search.dispatchEvent(new Event('input', { bubbles: true }));
    await flush({ status: 'backlog', q: 'Hades' });
    await harness.fixture.whenStable();
    expect(router.parseUrl(router.url).queryParams['q']).toBe('Hades');
    const sort = harness.routeNativeElement!.querySelector('#library-sort') as HTMLSelectElement;
    sort.value = 'release';
    sort.dispatchEvent(new Event('change', { bubbles: true }));
    await flush({ sort: 'release' });
    await harness.fixture.whenStable();
    expect(router.parseUrl(router.url).queryParams['sort']).toBe('release');
    // An external URL change uses the same queryParamMap path as browser back/forward.
    const previous = harness.navigateByUrl('/library?status=playing&q=Portal&sort=name');
    await flush({ status: 'playing', q: 'Portal', sort: 'name' });
    await previous;
    expect(search.value).toBe('Portal');
    expect(sort.value).toBe('name');
    expect(
      harness
        .routeNativeElement!.querySelector('[data-tab="playing"]')
        ?.getAttribute('aria-selected'),
    ).toBe('true');
  });
});
