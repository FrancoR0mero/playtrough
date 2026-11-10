import { PeopleSearchState } from '../../shared/components/people-search/people-search-state';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { UserSearchDto } from '../profile/models/user';
import { PeopleComponent } from './people';

const BEA: UserSearchDto = { username: 'bea', avatarUrl: null, relationship: { status: 'none' } };
describe('People search', () => {
  let fixture: ComponentFixture<PeopleComponent>;
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: ReturnType<typeof vi.spyOn>;
  const url = `${environment.apiUrl}/users/search`;
  const root = () => fixture.nativeElement as HTMLElement;
  const click = (label: string) => {
    [...root().querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === label)!
      .click();
    fixture.detectChanges();
  };
  function type(value: string) {
    TestBed.inject(PeopleSearchState).control.setValue(value);
    fixture.detectChanges();
  }
  function tick(ms = 400) {
    vi.advanceTimersByTime(ms);
    fixture.detectChanges();
  }
  function request(q = 'be', page = 1) {
    const req = http.expectOne((r) => r.url === url);
    expect(req.request.params.get('q')).toBe(q);
    expect(req.request.params.get('page')).toBe(String(page));
    return req;
  }
  function results(items = [BEA], page = 1, hasMore = false, q = 'be') {
    request(q, page).flush({ items, page, pageSize: 20, hasMore });
    fixture.detectChanges();
  }
  beforeEach(() => {
    vi.useFakeTimers();
    params = new BehaviorSubject(convertToParamMap({}));
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { ...auth, getAccessToken: () => null } },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: params, snapshot: { queryParamMap: params.value } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(PeopleComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    http.verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('offers an initial invitation and requires two characters', () => {
    expect(root().textContent).toContain('Buscá a otros jugadores');
    type('b');
    tick();
    http.expectNone(url);
    type(' be ');
    tick(399);
    http.expectNone(url);
    tick(1);
    results();
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { q: 'be' }, replaceUrl: true }),
    );
    expect(root().querySelector('[role="status"]')!.textContent).toContain('1 persona encontrada');
    expect(root().querySelector('app-user-avatar')).not.toBeNull();
    expect(root().querySelector('a[href="/profile/bea"]')).not.toBeNull();
  });
  it('restores URL queries immediately and does not repeat the request after URL synchronization', () => {
    params.next(convertToParamMap({ q: 'bea' }));
    tick(0);
    expect(TestBed.inject(PeopleSearchState).control.value).toBe('bea');
    results([BEA], 1, false, 'bea');
    type('cami');
    params.next(convertToParamMap({ q: 'cami' }));
    tick(399);
    http.expectNone(url);
    tick(1);
    results([], 1, false, 'cami');
    http.expectNone(url);
  });
  it('cancels old requests immediately and clears stale rows even during the next debounce', () => {
    type('be');
    tick();
    const old = request();
    type('cami');
    expect(old.cancelled).toBe(true);
    tick(399);
    http.expectNone(url);
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(0);
    tick(1);
    results([{ ...BEA, username: 'cami' }], 1, false, 'cami');
    type('ca');
    tick();
    const current = request('ca');
    type('');
    expect(current.cancelled).toBe(true);
    tick();
    http.expectNone(url);
    expect(root().textContent).toContain('Buscá a otros jugadores');
  });
  it('deduplicates pages and blocks concurrent load-more requests', () => {
    type('be');
    tick();
    results([BEA], 1, true);
    click('Cargar más');
    const second = request('be', 2);
    click('Cargar más');
    http.expectNone(url);
    second.flush({
      items: [
        { ...BEA, username: 'BEA' },
        { ...BEA, username: 'benja' },
      ],
      page: 2,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(2);
    expect(root().querySelector('[role="status"]')!.textContent).toContain(
      '2 personas encontradas',
    );
  });
  it('sends and cancels friendship requests from a compact result without losing the row', () => {
    type('be');
    tick();
    results();
    expect(root().querySelector('app-friendship-button > div')!.classList.contains('text-sm')).toBe(
      true,
    );
    click('Agregar amigo');
    const send = http.expectOne(`${environment.apiUrl}/friends/requests`);
    expect(send.request.body).toEqual({ username: 'bea' });
    send.flush({ id: 7, status: 'pending', user: BEA, createdAt: '2026-03-15T12:00:00Z' });
    fixture.detectChanges();
    expect(root().textContent).toContain('Solicitud enviada');
    click('Cancelar');
    const cancel = http.expectOne(`${environment.apiUrl}/friends/requests/7`);
    expect(cancel.request.method).toBe('DELETE');
    cancel.flush(null);
    fixture.detectChanges();
    expect(root().textContent).toContain('Agregar amigo');
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(1);
  });
  it('shows empty results and invalid_query as distinct states', () => {
    type('be');
    tick();
    results([]);
    expect(root().textContent).toContain('No encontramos personas');
    type('bad');
    tick();
    request('bad').flush({ code: 'invalid_query' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(root().querySelector('[role="alert"]')!.textContent).toContain(
      'La búsqueda no es válida',
    );
    expect(TestBed.inject(PeopleSearchState).invalid()).toBe(true);
    expect(root().textContent).not.toContain('Reintentar');
  });
  it('retains results on a later page error and retries that page', () => {
    type('be');
    tick();
    results([BEA], 1, true);
    click('Cargar más');
    request('be', 2).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(1);
    expect(root().querySelector('[role="alert"]')!.textContent).toContain('Revisá tu conexión');
    click('Reintentar');
    results([], 2);
    expect(root().querySelector('[role="alert"]')).toBeNull();
  });
  it('counts down 429 and requires an explicit retry after a query change', () => {
    type('be');
    tick();
    request().flush(
      {},
      { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
    );
    fixture.detectChanges();
    type('cami');
    tick(400);
    http.expectNone(url);
    expect(root().querySelector('button')!.disabled).toBe(true);
    tick(1600);
    expect(root().querySelector('button')!.disabled).toBe(false);
    click('Reintentar');
    results([], 1, false, 'cami');
  });
  it('protects the people route', () => {
    expect(routes.find((route) => route.path === 'people')?.canActivate).toEqual([authGuard]);
  });
});
