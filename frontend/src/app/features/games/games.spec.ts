import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { GAME_CARD_ASPECT_RATIO, GamesComponent } from './games';
import { GameSummary } from './models/game';

const GAMES: GameSummary[] = [
  {
    externalId: 1,
    name: 'Portal',
    slug: 'portal',
    coverUrl: 'https://example.test/portal.jpg',
    releaseYear: 2007,
    platforms: ['PC'],
  },
  {
    externalId: 2,
    name: 'Portal 2',
    slug: 'portal-2',
    coverUrl: null,
    releaseYear: 2011,
    platforms: ['PC'],
  },
];

describe('Game search screen', () => {
  let fixture: ComponentFixture<GamesComponent>;
  let http: HttpTestingController;
  let storage: TokenStorageService;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: ReturnType<typeof vi.spyOn>;
  const endpoint = `${environment.apiUrl}/games/search`;

  beforeEach(() => {
    vi.useFakeTimers();
    params = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [GamesComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: params.asObservable() } },
      ],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    storage.save(createSession());
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture = TestBed.createComponent(GamesComponent);
    fixture.detectChanges();
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

  function input(): HTMLInputElement {
    return fixture.nativeElement.querySelector('#game-search');
  }
  function type(value: string): void {
    input().value = value;
    input().dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }
  function request() {
    return http.expectOne((req) => req.url === endpoint);
  }
  function noRequest(): void {
    http.expectNone((req) => req.url === endpoint);
  }
  function cards(): HTMLButtonElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('[data-game-card]'));
  }
  function button(action: string): HTMLButtonElement | null {
    return fixture.nativeElement.querySelector(`[data-action="${action}"]`);
  }
  function flush(items = GAMES, hasMore = false, page = 1): void {
    request().flush({ items, hasMore, page, pageSize: 20 });
    fixture.detectChanges();
  }
  function search(): void {
    type('Portal');
    vi.advanceTimersByTime(400);
  }

  it('starts with an invitation and does not request queries shorter than two trimmed characters', () => {
    expect(fixture.nativeElement.textContent).toContain('Tu próxima aventura empieza acá');
    for (const q of ['', ' ', 'a', ' a ']) {
      type(q);
      vi.advanceTimersByTime(500);
    }
    noRequest();
    expect(cards()).toHaveLength(0);
  });

  it('debounces keystrokes for 400 ms and searches the final normalized query once', () => {
    type('Po');
    vi.advanceTimersByTime(250);
    type('Port');
    vi.advanceTimersByTime(250);
    type(' Portal ');
    vi.advanceTimersByTime(399);
    noRequest();
    vi.advanceTimersByTime(1);
    const pending = request();
    expect(pending.request.params.get('q')).toBe('Portal');
    expect(pending.request.params.get('page')).toBe('1');
    expect(pending.request.params.get('pageSize')).toBe('20');
    pending.flush({ items: GAMES, page: 1, pageSize: 20, hasMore: false });
    type('Portal  ');
    vi.advanceTimersByTime(500);
    noRequest();
  });

  it('cancels an older request immediately, before the next debounce expires', () => {
    search();
    const old = request();
    type('Hades');
    expect(old.cancelled).toBe(true);
    expect(() => old.flush({ items: GAMES })).toThrow();
    vi.advanceTimersByTime(399);
    noRequest();
    vi.advanceTimersByTime(1);
    const latest = request();
    expect(latest.request.params.get('q')).toBe('Hades');
    latest.flush({
      items: [{ ...GAMES[0], name: 'Hades', externalId: 3 }],
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(cards()).toHaveLength(1);
    expect(cards()[0].textContent).toContain('Hades');
    expect(fixture.nativeElement.textContent).not.toContain('Portal');
  });

  it('cancels a request and removes results when the query becomes too short', () => {
    search();
    const pending = request();
    type('P');
    expect(pending.cancelled).toBe(true);
    vi.advanceTimersByTime(500);
    noRequest();
    expect(fixture.nativeElement.querySelector('[data-skeleton]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Tu próxima aventura empieza acá');
  });

  it('writes and clears q using replaceUrl without adding a history entry per keystroke', () => {
    type(' Portal ');
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({
        queryParams: { q: 'Portal' },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      }),
    );
    type('');
    expect(navigate).toHaveBeenLastCalledWith(
      [],
      expect.objectContaining({ queryParams: { q: null } }),
    );
    vi.advanceTimersByTime(500);
    noRequest();
  });

  it('restores a query from route parameters without waiting for typing debounce', () => {
    params.next(convertToParamMap({ q: 'Portal' }));
    expect(input().value).toBe('Portal');
    vi.advanceTimersByTime(0);
    flush();
    expect(cards()).toHaveLength(2);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores its own URL update and handles navigation followed by a previously typed query', () => {
    search();
    flush();
    params.next(convertToParamMap({ q: 'Portal' }));
    vi.advanceTimersByTime(500);
    noRequest();
    params.next(convertToParamMap({ q: 'Hades' }));
    vi.advanceTimersByTime(0);
    flush([]);
    type('Portal');
    vi.advanceTimersByTime(400);
    const repeated = request();
    expect(repeated.request.params.get('q')).toBe('Portal');
    repeated.flush({ items: GAMES, page: 1, pageSize: 20, hasMore: false });
  });

  it('renders skeletons, then accessible result count, title, year and lazy centered images', () => {
    search();
    expect(fixture.nativeElement.querySelectorAll('[data-skeleton]')).toHaveLength(8);
    expect(fixture.nativeElement.querySelector('[aria-busy="true"]')).not.toBeNull();
    flush();
    expect(fixture.nativeElement.querySelector('[data-skeleton]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent).toContain(
      '2 resultados',
    );
    const card = cards()[0];
    expect(card.textContent).toContain('Portal');
    expect(card.textContent).toContain('2007');
    expect(card.style.aspectRatio).toBe(GAME_CARD_ASPECT_RATIO);
    const img = card.querySelector('img')!;
    expect(img.getAttribute('loading')).toBe('lazy');
    expect(img.classList.contains('object-cover')).toBe(true);
    expect(img.classList.contains('object-center')).toBe(true);
  });

  it('shows an empty state and announces zero results', () => {
    search();
    flush([]);
    expect(fixture.nativeElement.textContent).toContain('No encontramos juegos');
    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent).toContain(
      '0 resultados',
    );
    expect(button('load-more')).toBeNull();
  });

  it('appends pages without duplicates, prevents double requests and hides load more at the end', () => {
    search();
    flush([GAMES[0], GAMES[0]], true);
    expect(cards()).toHaveLength(1);
    button('load-more')!.click();
    button('load-more')!.click();
    fixture.detectChanges();
    expect(button('load-more')!.disabled).toBe(true);
    expect(cards()).toHaveLength(1);
    const next = request();
    expect(next.request.params.get('page')).toBe('2');
    noRequest();
    next.flush({ items: GAMES, page: 2, pageSize: 20, hasMore: false });
    fixture.detectChanges();
    expect(cards()).toHaveLength(2);
    expect(button('load-more')).toBeNull();
  });

  it('cancels load more and resets pagination and cards on a new query', () => {
    search();
    flush([GAMES[0]], true);
    button('load-more')!.click();
    const next = request();
    type('Hades');
    expect(next.cancelled).toBe(true);
    expect(cards()).toHaveLength(0);
    expect(button('load-more')).toBeNull();
    vi.advanceTimersByTime(400);
    const fresh = request();
    expect(fresh.request.params.get('page')).toBe('1');
    fresh.flush({ items: [], page: 1, pageSize: 20, hasMore: false });
  });

  it.each(['extensions', 'root'])(
    'shows invalid_query from %s as an accessible validation error',
    (location) => {
      search();
      const code = { code: 'invalid_query' };
      request().flush(location === 'extensions' ? { extensions: code } : code, {
        status: 400,
        statusText: 'Bad Request',
      });
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'La búsqueda no es válida',
      );
      expect(input().getAttribute('aria-invalid')).toBe('true');
      expect(input().getAttribute('aria-errormessage')).toBe('catalog-error');
      expect(button('retry')).toBeNull();
    },
  );

  it('shows catalog_unavailable and retries the same search once', () => {
    search();
    request().flush(
      { extensions: { code: 'catalog_unavailable' } },
      { status: 503, statusText: 'Unavailable' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'El catálogo no está disponible en este momento, probá más tarde',
    );
    button('retry')!.click();
    button('retry')!.click();
    flush();
    noRequest();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('preserves existing cards after a page failure and retries that page instead of skipping it', () => {
    search();
    flush([GAMES[0]], true);
    button('load-more')!.click();
    request().error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(cards()).toHaveLength(1);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Revisá tu conexión',
    );
    button('retry')!.click();
    const retried = request();
    expect(retried.request.params.get('page')).toBe('2');
    retried.flush({ items: [GAMES[1]], page: 2, pageSize: 20, hasMore: false });
    fixture.detectChanges();
    expect(cards()).toHaveLength(2);
  });

  it.each([0, 500])('shows a generic retryable error for status %s', (status) => {
    search();
    const pending = request();
    if (status === 0) pending.error(new ProgressEvent('error'));
    else pending.flush(null, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'No pudimos cargar los juegos',
    );
    button('retry')!.click();
    flush();
  });

  it.each([null, '2'])(
    'uses the shared 429 countdown with Retry-After %s and requires explicit retry',
    (header) => {
      search();
      request().flush(
        { extensions: { code: 'rate_limited' } },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: header ? { 'Retry-After': header } : {},
        },
      );
      fixture.detectChanges();
      const seconds = header ? 2 : 60;
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        `Demasiados intentos. Probá de nuevo en ${seconds} segundos`,
      );
      expect(button('retry')!.disabled).toBe(true);
      button('retry')!.click();
      vi.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(button('retry')!.textContent).toContain(`${seconds - 1} s`);
      vi.advanceTimersByTime((seconds - 1) * 1000);
      fixture.detectChanges();
      noRequest();
      expect(button('retry')!.disabled).toBe(false);
      expect(storage.readTokens()?.accessToken).toBe('access');
      button('retry')!.click();
      flush();
    },
  );

  it('keeps the countdown when typing a different query and retries only the latest text', () => {
    search();
    request().flush(null, {
      status: 429,
      statusText: 'Too Many Requests',
      headers: { 'Retry-After': '2' },
    });
    type('Hades');
    vi.advanceTimersByTime(2000);
    fixture.detectChanges();
    noRequest();
    button('retry')!.click();
    const pending = request();
    expect(pending.request.params.get('q')).toBe('Hades');
    pending.flush({ items: [], page: 1, pageSize: 20, hasMore: false });
  });

  it('renders placeholders for a null image and an image load error', () => {
    search();
    flush();
    expect(cards()[1].querySelector('[data-placeholder]')).not.toBeNull();
    const image = cards()[0].querySelector('img')!;
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(cards()[0].querySelector('img')).toBeNull();
    expect(cards()[0].querySelector('[data-placeholder]')).not.toBeNull();
  });

  it('cancels an active request on destruction', () => {
    search();
    const pending = request();
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
    vi.advanceTimersByTime(1000);
    noRequest();
  });

  it('clears the rate-limit interval on destruction', () => {
    search();
    const interval = vi.spyOn(globalThis, 'setInterval');
    const clear = vi.spyOn(globalThis, 'clearInterval');
    request().flush(null, { status: 429, statusText: 'Too Many Requests' });
    const countdown = interval.mock.results.at(-1)!.value;
    fixture.destroy();
    expect(clear).toHaveBeenCalledWith(countdown);
  });

  it('cancels a pending debounce on destruction', () => {
    type('Portal');
    vi.advanceTimersByTime(200);
    fixture.destroy();
    vi.advanceTimersByTime(1000);
    noRequest();
  });

  it('uses native keyboard-focusable buttons to open the known game in a single modal', async () => {
    search();
    flush();
    vi.useRealTimers();
    const card = cards()[0];
    expect(card.type).toBe('button');
    expect(card.tabIndex).toBe(0);
    expect(card.getAttribute('aria-haspopup')).toBe('dialog');
    expect(card.getAttribute('aria-label')).toBe('Ver detalle de Portal');
    card.focus();
    expect(document.activeElement).toBe(card);
    // Native buttons dispatch click for Enter/Space; jsdom does not synthesize that browser default.
    card.dispatchEvent(new MouseEvent('click', { bubbles: true, detail: 0 }));
    card.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.querySelectorAll('[role="dialog"]')).toHaveLength(1);
    const dialog = document.querySelector('[role="dialog"]')!;
    expect(dialog.textContent).toContain('Portal');
    expect(dialog.textContent).toContain('2007');
    expect(dialog.querySelector('img')?.getAttribute('src')).toBe(GAMES[0].coverUrl);
    expect(dialog.querySelector('a[href="https://rawg.io"]')).not.toBeNull();
    http.expectNone((req) => req.url.includes('/games/'));
  });

  it('traps focus, blocks scroll, closes on Escape and restores focus to the source card', async () => {
    search();
    flush();
    vi.useRealTimers();
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(100);
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(100);
    vi.spyOn(document.documentElement, 'scrollHeight', 'get').mockReturnValue(2000);
    vi.spyOn(window, 'scroll').mockImplementation(() => undefined);
    const card = cards()[0];
    card.focus();
    card.click();
    fixture.detectChanges();
    await fixture.whenStable();
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-labelledby')).toBe('game-preview-title');
    const close = dialog.querySelector('[data-action="close"]') as HTMLButtonElement;
    await vi.waitFor(() => expect(document.activeElement).toBe(close));
    const anchors = TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('.cdk-focus-trap-anchor');
    expect(anchors).toHaveLength(2);
    anchors[0].focus();
    expect(document.activeElement).toBe(dialog.querySelector('a'));
    anchors[1].focus();
    expect(document.activeElement).toBe(close);
    expect(document.documentElement.classList.contains('cdk-global-scrollblock')).toBe(true);
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }),
    );
    await fixture.whenStable();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(card);
    expect(document.documentElement.classList.contains('cdk-global-scrollblock')).toBe(false);
  });

  it('closes on backdrop click and opens another game without showing the previous game', async () => {
    search();
    flush();
    vi.useRealTimers();
    cards()[0].click();
    fixture.detectChanges();
    await fixture.whenStable();
    (document.querySelector('.cdk-overlay-backdrop') as HTMLElement).click();
    await fixture.whenStable();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    cards()[1].click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(document.querySelector('#game-preview-title')?.textContent?.trim()).toBe('Portal 2');
    expect(document.querySelector('[role="dialog"] [data-placeholder]')).not.toBeNull();
    (document.querySelector('[data-action="close"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.activeElement).toBe(cards()[1]);
  });
});
