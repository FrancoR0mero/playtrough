import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { GAME_SUMMARY_PREVIEW_LENGTH, GameDetailComponent } from './game-detail';
import { GameDetail, GameSummary } from './models/game';

const GAME: GameSummary = {
  externalId: 4200,
  name: 'Portal 2',
  slug: 'portal-2',
  releaseYear: 2011,
  coverUrl: 'https://example.test/card.jpg',
  platforms: ['PC'],
};
const DETAIL: GameDetail = {
  id: 17,
  externalId: 4200,
  name: 'Portal 2',
  slug: 'portal-2',
  summary: 'Una aventura de ingenio.\nResolvé los puzles con portales.',
  coverUrl: 'https://example.test/normal.jpg',
  coverUrlLarge: 'https://example.test/large.jpg',
  releaseDate: '2011-04-19',
  platforms: ['PC', 'PlayStation 3'],
  genres: ['Puzles', 'Aventura'],
  developers: ['Valve'],
};

describe('Game detail modal content', () => {
  let fixture: ComponentFixture<GameDetailComponent>;
  let http: HttpTestingController;
  let storage: TokenStorageService;
  const endpoint = `${environment.apiUrl}/games/${GAME.externalId}`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GameDetailComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: DIALOG_DATA, useValue: GAME },
        { provide: DialogRef, useValue: { close: vi.fn() } },
      ],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    storage.save(createSession());
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GameDetailComponent);
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

  function element<T extends Element = HTMLElement>(selector: string): T | null {
    return fixture.nativeElement.querySelector(selector);
  }
  function button(action: string): HTMLButtonElement | null {
    return element(`[data-action="${action}"]`);
  }
  function flush(detail: GameDetail = DETAIL): void {
    http.expectOne(endpoint).flush(detail);
    fixture.detectChanges();
  }
  function fail(status: number, code: string, headers = {}): void {
    http
      .expectOne(endpoint)
      .flush({ extensions: { code } }, { status, statusText: 'Error', headers });
    fixture.detectChanges();
  }
  function failImage(): void {
    element('img')!.dispatchEvent(new Event('error'));
    fixture.detectChanges();
  }

  it('requests the external ID once and shows the known title, image, year and skeleton while loading', () => {
    expect(element('h2')!.textContent).toContain(GAME.name);
    expect(fixture.nativeElement.textContent).toContain('2011');
    expect(element('img')!.getAttribute('src')).toBe(GAME.coverUrl);
    expect(element('[data-detail-skeleton]')).not.toBeNull();
    expect(element('[aria-busy="true"]')).not.toBeNull();
    expect(element('[role="status"]')!.textContent).toContain('Cargando detalle');
    const request = http.expectOne(endpoint);
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get('Authorization')).toBe('Bearer access');
    request.flush(DETAIL);
    fixture.detectChanges();
    expect(element('[data-detail-skeleton]')).toBeNull();
    expect(element('[aria-busy="true"]')).toBeNull();
    http.expectNone(endpoint);
  });

  it('renders complete detail, a Spanish calendar date and retains the local ID', () => {
    flush();
    expect(element('img')!.getAttribute('src')).toBe(DETAIL.coverUrlLarge);
    expect(element('time')!.textContent?.trim()).toBe('19 de abril de 2011');
    expect(element('time')!.getAttribute('datetime')).toBe('2011-04-19');
    for (const value of [...DETAIL.platforms, ...DETAIL.genres, ...DETAIL.developers]) {
      expect(fixture.nativeElement.textContent).toContain(value);
    }
    expect(element('#game-summary')!.textContent).toBe(DETAIL.summary);
    expect(fixture.componentInstance['detail']()?.id).toBe(17);
    expect(fixture.nativeElement.textContent).not.toContain('Agregar a mi biblioteca');
    expect(button('expand-summary')).toBeNull();
  });

  it('hides every absent section after loading, including the year known only from the card', () => {
    flush({
      ...DETAIL,
      releaseDate: null,
      platforms: [],
      genres: [],
      developers: [],
      summary: '  \n ',
    });
    expect(element('section')).toBeNull();
    expect(element('time')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('2011');
    expect(button('expand-summary')).toBeNull();
  });

  it('truncates a long summary, expands it and collapses it without another detail request', () => {
    const summary = 'Una aventura para descubrir. '.repeat(30);
    flush({ ...DETAIL, summary });
    const toggle = button('expand-summary')!;
    expect(element('#game-summary')!.textContent).toBe(
      summary.slice(0, GAME_SUMMARY_PREVIEW_LENGTH).trimEnd() + '…',
    );
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    expect(toggle.getAttribute('aria-controls')).toBe('game-summary');
    toggle.click();
    fixture.detectChanges();
    expect(element('#game-summary')!.textContent).toBe(summary.trim());
    expect(toggle.textContent).toContain('Ver menos');
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    toggle.click();
    fixture.detectChanges();
    expect(element('#game-summary')!.textContent).toHaveLength(GAME_SUMMARY_PREVIEW_LENGTH + 1);
    expect(toggle.textContent).toContain('Ver más');
    http.expectNone(endpoint);
  });

  it('does not truncate a summary at exactly the preview length', () => {
    const summary = 'a'.repeat(GAME_SUMMARY_PREVIEW_LENGTH);
    flush({ ...DETAIL, summary });
    expect(element('#game-summary')!.textContent).toBe(summary);
    expect(button('expand-summary')).toBeNull();
  });

  it('renders HTML-like summary as literal text, including after expansion', () => {
    const summary =
      '<b>hola</b> <img src=x onerror="alert(1)"> ' +
      'Texto seguro. '.repeat(60) +
      '<script>alert(1)</script>';
    flush({ ...DETAIL, summary });
    expect(element('#game-summary')!.textContent).toContain('<b>hola</b>');
    expect(element('#game-summary b, #game-summary img, #game-summary script')).toBeNull();
    button('expand-summary')!.click();
    fixture.detectChanges();
    expect(element('#game-summary')!.textContent).toBe(summary);
    expect(element('#game-summary b, #game-summary img, #game-summary script')).toBeNull();
  });

  it('falls back from the large image to the normal image and then to a placeholder on errors', () => {
    flush();
    failImage();
    expect(element('img')!.getAttribute('src')).toBe(DETAIL.coverUrl);
    failImage();
    expect(element('img')).toBeNull();
    expect(element('[data-placeholder]')).not.toBeNull();
  });

  it('uses the normal image when the large one is missing', () => {
    flush({ ...DETAIL, coverUrlLarge: null });
    expect(element('img')!.getAttribute('src')).toBe(DETAIL.coverUrl);
  });

  it('shows the placeholder when both detail images are missing', () => {
    flush({ ...DETAIL, coverUrl: null, coverUrlLarge: null });
    expect(element('img')).toBeNull();
    expect(element('[data-placeholder]')).not.toBeNull();
  });

  it('does not loop when large and normal images have the same failing URL', () => {
    flush({ ...DETAIL, coverUrl: DETAIL.coverUrlLarge });
    failImage();
    expect(element('img')).toBeNull();
    expect(element('[data-placeholder]')).not.toBeNull();
  });

  it('can show a new detail image even if the initial card image failed', () => {
    failImage();
    expect(element('[data-placeholder]')).not.toBeNull();
    flush();
    expect(element('img')!.getAttribute('src')).toBe(DETAIL.coverUrlLarge);
  });

  it('shows game_not_found without retrying a permanently missing game', () => {
    fail(404, 'game_not_found');
    expect(element('[role="alert"]')!.textContent).toContain('No encontramos este juego');
    expect(element('[data-detail-skeleton]')).toBeNull();
    expect(button('retry-detail')).toBeNull();
  });

  it('shows catalog_unavailable and retries the same external ID without duplicate requests', () => {
    fail(503, 'catalog_unavailable');
    expect(element('[role="alert"]')!.textContent).toContain(
      'El catálogo no está disponible en este momento, probá más tarde',
    );
    button('retry-detail')!.click();
    button('retry-detail')!.click();
    fixture.detectChanges();
    expect(element('[data-detail-skeleton]')).not.toBeNull();
    flush();
    http.expectNone(endpoint);
    expect(element('[role="alert"]')).toBeNull();
    expect(element('#game-summary')).not.toBeNull();
  });

  it('also accepts ProblemDetails code at the root', () => {
    http
      .expectOne(endpoint)
      .flush({ code: 'catalog_unavailable' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(element('[role="alert"]')!.textContent).toContain('El catálogo no está disponible');
  });

  it.each([0, 500])('shows a retryable generic error for status %s', (status) => {
    const request = http.expectOne(endpoint);
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush(null, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(element('[role="alert"]')!.textContent).toContain('No pudimos cargar el detalle');
    button('retry-detail')!.click();
    flush();
  });

  it.each([null, '2'])(
    'reuses the 429 countdown with Retry-After %s and waits for an explicit retry',
    (header) => {
      vi.useFakeTimers();
      fail(429, 'rate_limited', header ? { 'Retry-After': header } : {});
      const seconds = header ? 2 : 60;
      expect(element('[role="alert"]')!.textContent).toContain(
        `Demasiados intentos. Probá de nuevo en ${seconds} segundos`,
      );
      expect(button('retry-detail')!.disabled).toBe(true);
      button('retry-detail')!.click();
      http.expectNone(endpoint);
      vi.advanceTimersByTime(1000);
      fixture.detectChanges();
      expect(button('retry-detail')!.textContent).toContain(`${seconds - 1} s`);
      vi.advanceTimersByTime((seconds - 1) * 1000);
      fixture.detectChanges();
      expect(button('retry-detail')!.disabled).toBe(false);
      http.expectNone(endpoint);
      expect(storage.readTokens()?.accessToken).toBe('access');
      button('retry-detail')!.click();
      flush();
    },
  );

  it('leaves 401 session expiry to the existing interceptor without a catalog error', () => {
    http.expectOne(endpoint).flush(null, { status: 401, statusText: 'Unauthorized' });
    http
      .expectOne(`${environment.apiUrl}/auth/refresh`)
      .flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(element('[role="alert"]')).toBeNull();
    expect(storage.readTokens()).toBeNull();
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalled();
  });

  it('cancels an unfinished detail request when the component is destroyed', () => {
    const request = http.expectOne(endpoint);
    fixture.destroy();
    expect(request.cancelled).toBe(true);
  });

  it('clears its countdown interval when the modal is destroyed', () => {
    vi.useFakeTimers();
    const interval = vi.spyOn(globalThis, 'setInterval');
    const clear = vi.spyOn(globalThis, 'clearInterval');
    fail(429, 'rate_limited');
    const countdown = interval.mock.results.at(-1)!.value;
    fixture.destroy();
    expect(clear).toHaveBeenCalledWith(countdown);
  });

  it('keeps an active RAWG credit inside the modal', () => {
    flush();
    const link = element('a[href="https://rawg.io"]')!;
    expect(link.getAttribute('target')).toBe('_blank');
    expect(link.getAttribute('rel')).toBe('noopener noreferrer');
  });
});
