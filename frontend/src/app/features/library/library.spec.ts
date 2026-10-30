import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { localCalendarDate } from './library-date';
import { EMPTY_LIBRARY, LIBRARY_COUNTS, LIBRARY_GAMES } from './library-test-helpers';
import { LibraryComponent } from './library';
import { LibraryResponse } from './models/library';
import { LibraryService } from './services/library';

describe('Personal library screen', () => {
  let fixture: ComponentFixture<LibraryComponent>;
  let http: HttpTestingController;
  let storage: TokenStorageService;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: ReturnType<typeof vi.spyOn>;
  const url = `${environment.apiUrl}/library`;
  beforeEach(() => {
    vi.useFakeTimers();
    params = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [LibraryComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: params.asObservable() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    storage = TestBed.inject(TokenStorageService);
    storage.save(createSession());
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(LibraryComponent);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });
  function start(query = {}): void {
    params.next(convertToParamMap(query));
    fixture.detectChanges();
  }
  function request() {
    return http.expectOne((req) => req.url === url && req.method === 'GET');
  }
  function noRequest(): void {
    http.expectNone((req) => req.url === url && req.method === 'GET');
  }
  function flush(items = LIBRARY_GAMES, extra: Partial<LibraryResponse> = {}): void {
    request().flush({
      items,
      counts: LIBRARY_COUNTS,
      page: 1,
      pageSize: 20,
      hasMore: false,
      ...extra,
    });
    fixture.detectChanges();
  }
  function element<T extends Element = HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector);
  }
  function button(action: string): HTMLButtonElement {
    return element<HTMLButtonElement>(`[data-action="${action}"]`)!;
  }
  function click(action: string): void {
    button(action).click();
    fixture.detectChanges();
  }
  function cards(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-game-card]'));
  }
  function tab(value: string): HTMLButtonElement {
    return element<HTMLButtonElement>(`[data-tab="${value}"]`)!;
  }
  function type(q: string): void {
    const input = element<HTMLInputElement>('#library-search')!;
    input.value = q;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }
  function openMenu(index = 0): void {
    (
      fixture.nativeElement.querySelectorAll('[data-library-menu]')[index] as HTMLButtonElement
    ).click();
    fixture.detectChanges();
  }
  function choose(status: string, index = 0): void {
    openMenu(index);
    element<HTMLButtonElement>(`[role="menu"] [data-status="${status}"]`)!.click();
    fixture.detectChanges();
  }

  it('renders global counters, matching cards and an accessible result count', () => {
    start();
    expect(element('[data-library-skeleton]')).not.toBeNull();
    const pending = request();
    expect(pending.request.params.get('sort')).toBe('recent');
    expect(pending.request.params.get('page')).toBe('1');
    expect(pending.request.params.get('pageSize')).toBe('20');
    expect(pending.request.params.has('status')).toBe(false);
    pending.flush({
      ...EMPTY_LIBRARY,
      items: LIBRARY_GAMES,
      counts: { playing: 4, backlog: 3, beaten: 2, completed: 1, abandoned: 0, total: 10 },
    });
    fixture.detectChanges();
    for (const [status, label, count] of [
      ['all', 'Todos', 10],
      ['playing', 'Jugando', 4],
      ['backlog', 'Por jugar', 3],
      ['beaten', 'Terminados', 2],
      ['completed', 'Completados', 1],
      ['abandoned', 'Abandonados', 0],
    ]) {
      expect(tab(String(status)).textContent).toContain(label);
      expect(tab(String(status)).textContent).toContain(String(count));
    }
    expect(cards()).toHaveLength(2);
    expect(cards()[0].textContent).toContain('Portal 2');
    expect(cards()[0].textContent).toContain('2011');
    expect(cards()[0].querySelector('[data-library-badge]')?.textContent).toBe('Jugando');
    expect(element('[data-library-results]')?.textContent).toContain('2 juegos');
    expect(element('[data-library-results]')?.getAttribute('aria-live')).toBe('polite');
  });

  it('restores filters from a URL and ignores its own navigation echo', () => {
    start({ status: 'beaten', q: ' Portal ', sort: 'name' });
    const pending = request();
    expect(pending.request.params.get('status')).toBe('beaten');
    expect(pending.request.params.get('q')).toBe('Portal');
    expect(pending.request.params.get('sort')).toBe('name');
    pending.flush({ ...EMPTY_LIBRARY, counts: LIBRARY_COUNTS });
    fixture.detectChanges();
    expect(element<HTMLInputElement>('#library-search')?.value).toBe('Portal');
    expect(element<HTMLSelectElement>('#library-sort')?.value).toBe('name');
    expect(tab('beaten').getAttribute('aria-selected')).toBe('true');
    tab('backlog').click();
    fixture.detectChanges();
    const selected = request();
    expect(selected.request.params.get('status')).toBe('backlog');
    selected.flush({ ...EMPTY_LIBRARY, counts: LIBRARY_COUNTS });
    params.next(convertToParamMap({ status: 'backlog', q: 'Portal', sort: 'name' }));
    noRequest();
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { status: 'backlog', q: 'Portal', sort: 'name' } }),
    );
  });

  it('falls back to Todos and recent for unknown URL values', () => {
    start({ status: 'invalid', sort: 'invalid' });
    const pending = request();
    expect(pending.request.params.has('status')).toBe(false);
    expect(pending.request.params.get('sort')).toBe('recent');
    pending.flush(EMPTY_LIBRARY);
  });

  it('switches tabs with arrows and Home/End, synchronizes URL and resets paging', () => {
    start();
    flush();
    tab('all').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();
    expect(document.activeElement).toBe(tab('playing'));
    expect(tab('playing').getAttribute('aria-selected')).toBe('true');
    const playing = request();
    expect(playing.request.params.get('status')).toBe('playing');
    playing.flush({ ...EMPTY_LIBRARY, items: [LIBRARY_GAMES[0]], counts: LIBRARY_COUNTS });
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { status: 'playing', q: null, sort: null } }),
    );
    tab('playing').dispatchEvent(new KeyboardEvent('keydown', { key: 'End', bubbles: true }));
    fixture.detectChanges();
    flush([]);
    expect(document.activeElement).toBe(tab('abandoned'));
    tab('abandoned').dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    fixture.detectChanges();
    const all = request();
    expect(all.request.params.has('status')).toBe(false);
    all.flush(EMPTY_LIBRARY);
  });

  it('debounces search for 400 ms and cancels old requests immediately', () => {
    start();
    const initial = request();
    type('Po');
    expect(initial.cancelled).toBe(true);
    vi.advanceTimersByTime(250);
    type('Portal');
    vi.advanceTimersByTime(399);
    noRequest();
    vi.advanceTimersByTime(1);
    const old = request();
    expect(old.request.params.get('q')).toBe('Portal');
    type('Hades');
    expect(old.cancelled).toBe(true);
    vi.advanceTimersByTime(400);
    const latest = request();
    expect(latest.request.params.get('q')).toBe('Hades');
    latest.flush({ ...EMPTY_LIBRARY, items: [LIBRARY_GAMES[1]], counts: LIBRARY_COUNTS });
    fixture.detectChanges();
    expect(cards()).toHaveLength(1);
    expect(cards()[0].textContent).toContain('Hades');
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({
        queryParams: { status: null, q: 'Hades', sort: null },
        replaceUrl: true,
      }),
    );
    type('Hades ');
    vi.advanceTimersByTime(500);
    noRequest();
    type('');
    vi.advanceTimersByTime(400);
    const cleared = request();
    expect(cleared.request.params.has('q')).toBe(false);
    cleared.flush(EMPTY_LIBRARY);
  });

  it.each(['added', 'name', 'release', 'recent'])('requests and reflects sort %s', (sort) => {
    start({ sort: 'name' });
    flush();
    const select = element<HTMLSelectElement>('#library-sort')!;
    if (sort === 'name') {
      noRequest();
      return;
    }
    select.value = sort;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
    const pending = request();
    expect(pending.request.params.get('sort')).toBe(sort);
    pending.flush(EMPTY_LIBRARY);
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({
        queryParams: { status: null, q: null, sort: sort === 'recent' ? null : sort },
      }),
    );
  });

  it('loads more once, deduplicates by local ID and hides the button on the last page', () => {
    start();
    flush([LIBRARY_GAMES[0]], { hasMore: true });
    click('load-more');
    button('load-more').click();
    const pending = request();
    expect(pending.request.params.get('page')).toBe('2');
    pending.flush({ ...EMPTY_LIBRARY, items: LIBRARY_GAMES, counts: LIBRARY_COUNTS, page: 2 });
    fixture.detectChanges();
    expect(cards()).toHaveLength(2);
    expect(button('load-more')).toBeNull();
    noRequest();
  });

  it.each([
    [{}, EMPTY_LIBRARY.counts, 'Todavía no agregaste juegos'],
    [{ status: 'completed' }, LIBRARY_COUNTS, 'No tenés juegos en este estado'],
    [{ q: 'Unknown' }, LIBRARY_COUNTS, 'No encontramos juegos para esta búsqueda'],
  ])('distinguishes empty states for %j', (query, counts, message) => {
    start(query);
    flush([], { counts });
    expect(element('[data-library-empty]')?.textContent).toContain(message);
    expect(element('[data-library-empty] a[href="/games?focus=search"]') !== null).toBe(
      counts.total === 0,
    );
    expect(element('.library-heading a[href="/games?focus=search"]')).not.toBeNull();
  });

  it('renders invalid_query as an alert without retrying the same invalid input', () => {
    start({ q: 'bad' });
    request().flush(
      { extensions: { code: 'invalid_query' } },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('La búsqueda no es válida');
    expect(element('#library-search')?.getAttribute('aria-invalid')).toBe('true');
    expect(button('retry-library-list')).toBeNull();
    type('Portal');
    vi.advanceTimersByTime(400);
    flush();
    expect(element('[role="alert"]')).toBeNull();
  });

  it('keeps cards after a network page failure and retries the failed page', () => {
    start();
    flush([LIBRARY_GAMES[0]], { hasMore: true });
    click('load-more');
    request().error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(cards()).toHaveLength(1);
    expect(element('[role="alert"]')?.textContent).toContain('Revisá tu conexión');
    click('retry-library-list');
    const retry = request();
    expect(retry.request.params.get('page')).toBe('2');
    retry.flush({ ...EMPTY_LIBRARY, items: [LIBRARY_GAMES[1]], counts: LIBRARY_COUNTS, page: 2 });
    fixture.detectChanges();
    expect(cards()).toHaveLength(2);
  });

  it('uses Retry-After for list failures and waits for manual retry', () => {
    start();
    request().flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': '2' },
    });
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('2 segundos');
    expect(button('retry-library-list').disabled).toBe(true);
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(button('retry-library-list').textContent).toContain('1 s');
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    noRequest();
    click('retry-library-list');
    flush();
    expect(element('[role="alert"]')).toBeNull();
  });

  it.each([
    ['beaten', 'Terminado'],
    ['abandoned', 'Abandonado'],
  ] as const)('updates badge and counts after changing to %s (%s)', (status, label) => {
    start({ sort: 'name' });
    flush();
    choose(status);
    const pending = http.expectOne(`${url}/17/status`);
    expect(pending.request.body).toEqual({ status, occurredOn: localCalendarDate() });
    expect(element<HTMLButtonElement>('[data-library-menu]')?.disabled).toBe(true);
    pending.flush({ ...LIBRARY_GAMES[0], status });
    fixture.detectChanges();
    expect(cards()[0].querySelector('[data-library-badge]')?.textContent).toBe(label);
    expect(tab('playing').textContent).toContain('0');
    expect(tab(status).textContent).toContain('1');
    expect(tab('all').textContent).toContain('2');
    expect(element('[data-action-notice]')?.textContent).toContain('Estado actualizado');
    noRequest();
  });

  it('removes a moved game from a filtered tab and restores focus to that tab', () => {
    start({ status: 'playing' });
    flush([LIBRARY_GAMES[0]]);
    choose('completed');
    http.expectOne(`${url}/17/status`).flush({ ...LIBRARY_GAMES[0], status: 'completed' });
    fixture.detectChanges();
    expect(cards()).toHaveLength(0);
    expect(tab('playing').textContent).toContain('0');
    expect(tab('completed').textContent).toContain('1');
    expect(document.activeElement).toBe(tab('playing'));
    noRequest();
  });

  it.each([
    ['already_in_library', 'ya está en tu biblioteca'],
    ['not_in_library', 'ya no está'],
    ['invalid_status', 'Elegí un estado válido'],
    ['invalid_date', 'Revisá la fecha'],
    ['date_before_previous_event', 'último cambio (26/10/2026)'],
    ['game_not_found', 'No encontramos este juego'],
    ['concurrent_modification', 'volvé a intentar'],
    ['unknown_code', 'Revisá tu conexión'],
  ])('shows %s from a menu mutation and reloads the list', (code, message) => {
    start();
    flush();
    choose('beaten');
    http
      .expectOne(`${url}/17/status`)
      .flush({ extensions: { code, minDate: '2026-10-26' } }, { status: 400, statusText: 'Error' });
    fixture.detectChanges();
    expect(element('[data-action-error]')?.textContent).toContain(message);
    flush();
    expect(element('[data-action-error]')?.textContent).toContain(message);
    expect(cards()[0].querySelector('[data-library-badge]')?.textContent).toBe('Jugando');
  });

  it('reloads on network mutation failure', () => {
    start();
    flush();
    choose('beaten');
    http.expectOne(`${url}/17/status`).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(element('[data-action-error]')?.textContent).toContain('Revisá tu conexión');
    flush();
  });

  it('delays a post-error refresh after 429, and never retries the write', () => {
    start();
    flush();
    choose('beaten');
    http.expectOne(`${url}/17/status`).flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': '2' },
    });
    fixture.detectChanges();
    expect(element('[role="alert"]')?.textContent).toContain('2 segundos');
    noRequest();
    vi.advanceTimersByTime(1999);
    noRequest();
    vi.advanceTimersByTime(1);
    flush();
    http.expectNone(`${url}/17/status`);
    expect(element('[data-action-error]')?.textContent).toContain('No se guardó el cambio');
  });

  it('rebuilds the loaded prefix after a mutation before requesting the next offset page', () => {
    start({ sort: 'name' });
    flush([LIBRARY_GAMES[0]], { hasMore: true });
    choose('completed');
    http.expectOne(`${url}/17/status`).flush({ ...LIBRARY_GAMES[0], status: 'completed' });
    fixture.detectChanges();
    click('load-more');
    const first = request();
    expect(first.request.params.get('page')).toBe('1');
    first.flush({
      ...EMPTY_LIBRARY,
      items: [LIBRARY_GAMES[1]],
      counts: LIBRARY_COUNTS,
      hasMore: true,
    });
    const second = request();
    expect(second.request.params.get('page')).toBe('2');
    second.flush({
      ...EMPTY_LIBRARY,
      items: [{ ...LIBRARY_GAMES[0], status: 'completed' }, LIBRARY_GAMES[1]],
      counts: LIBRARY_COUNTS,
      page: 2,
    });
    fixture.detectChanges();
    expect(cards()).toHaveLength(2);
    expect(cards()[0].textContent).toContain('Hades');
    expect(button('load-more')).toBeNull();
  });

  it('reloads instead of guessing a previous status when an unknown game changes', () => {
    start();
    flush();
    TestBed.inject(LibraryService).changeStatus(99, 'completed').subscribe();
    http
      .expectOne(`${url}/99/status`)
      .flush({ ...LIBRARY_GAMES[0], gameId: 99, status: 'completed' });
    flush();
  });

  it('cancels a pending list response when a service change arrives', () => {
    start();
    const old = request();
    TestBed.inject(LibraryService).remove(17).subscribe();
    http.expectOne(`${url}/17`).flush(null);
    expect(old.cancelled).toBe(true);
    flush([LIBRARY_GAMES[1]], { counts: { ...EMPTY_LIBRARY.counts, backlog: 1, total: 1 } });
    expect(cards()).toHaveLength(1);
  });

  it('cancels pending searches and unsubscribes from changes when destroyed', () => {
    start();
    flush();
    type('Portal');
    fixture.destroy();
    vi.advanceTimersByTime(400);
    noRequest();
    TestBed.inject(LibraryService).remove(17).subscribe();
    http.expectOne(`${url}/17`).flush(null);
    noRequest();
  });

  it('confirms removal in the shared dialog, cancels without DELETE, and updates counters after confirmation', async () => {
    start();
    flush();
    vi.useRealTimers();
    openMenu();
    click('menu-remove');
    await fixture.whenStable();
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Portal 2');
    expect(dialog.getAttribute('aria-labelledby')).toBe('library-removal-title');
    expect(document.activeElement).toBe(dialog.querySelector('[data-action="cancel"]'));
    (dialog.querySelector('[data-action="cancel"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    http.expectNone(`${url}/17`);
    openMenu();
    click('menu-remove');
    await fixture.whenStable();
    (document.querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
    http.expectOne(`${url}/17`).flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(cards()).toHaveLength(1);
    expect(tab('all').textContent).toContain('1');
    expect(tab('playing').textContent).toContain('0');
    expect(element('[data-action-notice]')?.textContent).toContain('Quitado de tu biblioteca');
  });

  it('opens detail with externalId and receives changes from its library section', async () => {
    start({ sort: 'name' });
    flush();
    vi.useRealTimers();
    const origin = cards()[0];
    origin.focus();
    origin.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 0 }));
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/games/4200`).flush({
      ...LIBRARY_GAMES[0],
      id: 17,
      coverUrlLarge: null,
      releaseDate: null,
      summary: '',
      platforms: [],
      genres: [],
      developers: [],
    });
    fixture.detectChanges();
    http.expectOne(`${url}/games/17`).flush(LIBRARY_GAMES[0]);
    await fixture.whenStable();
    (document.querySelector('[data-action="choose-beaten"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (document.querySelector('[data-action="save-library"]') as HTMLButtonElement).click();
    http.expectOne(`${url}/17/status`).flush({ ...LIBRARY_GAMES[0], status: 'beaten' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(cards()[0].querySelector('[data-library-badge]')?.textContent).toBe('Terminado');
    expect(tab('playing').textContent).toContain('0');
    expect(tab('beaten').textContent).toContain('1');
    noRequest();
    (document.querySelector('[data-action="close"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(origin);
  });

  it('keeps counts unchanged for an idempotent status change', () => {
    start({ sort: 'name' });
    flush();
    TestBed.inject(LibraryService).changeStatus(17, 'playing').subscribe();
    http.expectOne(`${url}/17/status`).flush(LIBRARY_GAMES[0]);
    fixture.detectChanges();
    expect(tab('playing').textContent).toContain('1');
    expect(tab('all').textContent).toContain('2');
    noRequest();
  });

  it('shows optimistic changes while refreshing the server recent order without replacing existing cards', () => {
    start();
    flush();
    const origin = cards()[0];
    choose('completed', 1);
    http.expectOne(`${url}/18/status`).flush({ ...LIBRARY_GAMES[1], status: 'completed' });
    fixture.detectChanges();
    expect(tab('backlog').textContent).toContain('0');
    expect(tab('completed').textContent).toContain('1');
    expect(cards()[1].textContent).toContain('Completado');
    const refreshed = request();
    expect(refreshed.request.params.get('sort')).toBe('recent');
    refreshed.flush({
      ...EMPTY_LIBRARY,
      items: [{ ...LIBRARY_GAMES[1], status: 'completed' }, LIBRARY_GAMES[0]],
      counts: { ...LIBRARY_COUNTS, backlog: 0, completed: 1 },
    });
    fixture.detectChanges();
    expect(cards()[0].textContent).toContain('Hades');
    expect(cards()[1]).toBe(origin);
  });

  it('increments total and state counts on additions, then obtains the ordered list', () => {
    start({ sort: 'name' });
    flush();
    const added = {
      ...LIBRARY_GAMES[0],
      gameId: 19,
      externalId: 88,
      name: 'Celeste',
      status: 'completed' as const,
    };
    TestBed.inject(LibraryService).add(19, 'completed').subscribe();
    http.expectOne((req) => req.method === 'POST' && req.url === url).flush(added);
    fixture.detectChanges();
    expect(tab('all').textContent).toContain('3');
    expect(tab('completed').textContent).toContain('1');
    flush([added, LIBRARY_GAMES[1], LIBRARY_GAMES[0]], {
      counts: { ...LIBRARY_COUNTS, completed: 1, abandoned: 0, total: 3 },
    });
    expect(cards()[0].textContent).toContain('Celeste');
  });

  it('reloads instead of allowing inconsistent counts to go negative', () => {
    start();
    flush([LIBRARY_GAMES[0]], { counts: { ...EMPTY_LIBRARY.counts, backlog: 1, total: 1 } });
    TestBed.inject(LibraryService).remove(17).subscribe();
    http.expectOne(`${url}/17`).flush(null);
    flush([], { counts: EMPTY_LIBRARY.counts });
    expect(element('[data-library-empty]')?.textContent).toContain('Todavía no agregaste juegos');
  });

  it('reloads after a modal conflict and restores focus to the refreshed source card', async () => {
    start();
    flush();
    vi.useRealTimers();
    cards()[0].click();
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/games/4200`).flush({
      ...LIBRARY_GAMES[0],
      id: 17,
      coverUrlLarge: null,
      releaseDate: null,
      summary: '',
      platforms: [],
      genres: [],
      developers: [],
    });
    fixture.detectChanges();
    http.expectOne(`${url}/games/17`).flush(LIBRARY_GAMES[0]);
    await fixture.whenStable();
    (document.querySelector('[data-action="save-library"]') as HTMLButtonElement).click();
    http
      .expectOne(`${url}/17/status`)
      .flush(
        { extensions: { code: 'concurrent_modification' } },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();
    const changed = { ...LIBRARY_GAMES[0], status: 'completed' as const };
    http.expectOne(`${url}/games/17`).flush(changed);
    flush([changed, LIBRARY_GAMES[1]], { counts: { ...LIBRARY_COUNTS, playing: 0, completed: 1 } });
    await fixture.whenStable();
    expect(tab('completed').textContent).toContain('1');
    expect(document.querySelector('app-library-entry [role="alert"]')?.textContent).toContain(
      'volvé a intentar',
    );
    (document.querySelector('[data-action="close"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(cards()[0]);
  });

  it('returns focus to the active tab after the modal removes its source card from that tab', async () => {
    start({ status: 'playing' });
    flush([LIBRARY_GAMES[0]]);
    vi.useRealTimers();
    cards()[0].click();
    fixture.detectChanges();
    http.expectOne(`${environment.apiUrl}/games/4200`).flush({
      ...LIBRARY_GAMES[0],
      id: 17,
      coverUrlLarge: null,
      releaseDate: null,
      summary: '',
      platforms: [],
      genres: [],
      developers: [],
    });
    fixture.detectChanges();
    http.expectOne(`${url}/games/17`).flush(LIBRARY_GAMES[0]);
    await fixture.whenStable();
    (document.querySelector('[data-action="choose-completed"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (document.querySelector('[data-action="save-library"]') as HTMLButtonElement).click();
    http.expectOne(`${url}/17/status`).flush({ ...LIBRARY_GAMES[0], status: 'completed' });
    await fixture.whenStable();
    expect(cards()).toHaveLength(0);
    (document.querySelector('[data-action="close"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(tab('playing'));
  });
});
