import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { GameReviewsComponent } from './game-reviews';
import { MyReviewDto, PublicReviewDto, ReviewListResponse } from './models/review';
import { ReviewService } from './services/review';

const DETAIL = {
  id: 17,
  externalId: 4200,
  name: 'Portal 2',
  coverUrl: 'https://example.test/portal.jpg',
  ratingSummary: { average: 4.3, count: 12 },
};
const MINE: MyReviewDto = {
  id: 1,
  gameId: 17,
  rating: 4,
  text: 'Mi opinión',
  createdAt: '2026-11-01T12:00:00Z',
  editedAt: '2026-11-02T12:00:00Z',
};
const PUBLIC: PublicReviewDto = {
  id: 2,
  rating: 5,
  text: 'Excelente\nCon amigos',
  createdAt: '2026-11-01T12:00:00Z',
  editedAt: null,
  author: { userId: 2, username: 'otro' },
  isMine: false,
};

describe('Game reviews screen', () => {
  let fixture: ComponentFixture<GameReviewsComponent>;
  let http: HttpTestingController;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let query: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let navigate: ReturnType<typeof vi.spyOn>;
  const detailUrl = `${environment.apiUrl}/games/4200`;
  const mineUrl = `${environment.apiUrl}/games/17/reviews/me`;
  const listUrl = `${environment.apiUrl}/games/17/reviews`;
  const el = <T extends HTMLElement = HTMLElement>(selector: string) =>
    fixture.nativeElement.querySelector(selector) as T;
  const button = (action: string) => el<HTMLButtonElement>(`[data-action="${action}"]`);
  function click(action: string) {
    button(action).click();
    fixture.detectChanges();
  }
  function listRequest() {
    return http.expectOne((req) => req.url === listUrl);
  }
  function flushList(items: PublicReviewDto[] = [PUBLIC], extra: Partial<ReviewListResponse> = {}) {
    listRequest().flush({
      items,
      ratingSummary: DETAIL.ratingSummary,
      page: 1,
      pageSize: 20,
      hasMore: false,
      ...extra,
    });
    fixture.detectChanges();
  }
  function flushMine(mine: MyReviewDto | null = null) {
    const req = http.expectOne(mineUrl);
    if (mine) req.flush(mine);
    else
      req.flush(
        { extensions: { code: 'review_not_found' } },
        { status: 404, statusText: 'Not Found' },
      );
    fixture.detectChanges();
  }
  function start(sort = 'recent') {
    query.next(convertToParamMap({ sort }));
    fixture.detectChanges();
  }
  function detail() {
    http.expectOne(detailUrl).flush(DETAIL);
    fixture.detectChanges();
  }
  function ready() {
    start();
    detail();
    flushMine();
    flushList();
  }
  function sort(value: string) {
    const select = el<HTMLSelectElement>('select');
    select.value = value;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  it('links review authors to their public profile with a reusable avatar', () => {
    ready();
    const author = el<HTMLAnchorElement>('[data-review-id="2"] a');
    expect(author.getAttribute('href')).toBe('/profile/otro');
    expect(author.textContent).toContain('otro');
    expect(author.querySelector('app-user-avatar')).not.toBeNull();
  });

  beforeEach(() => {
    params = new BehaviorSubject(convertToParamMap({ externalId: '4200' }));
    query = new BehaviorSubject(convertToParamMap({}));
    TestBed.configureTestingModule({
      imports: [GameReviewsComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: params.asObservable(), queryParamMap: query.asObservable() },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi
      .spyOn(TestBed.inject(Router), 'navigate')
      .mockImplementation(async (_commands, extras) => {
        if (extras?.queryParams) query.next(convertToParamMap(extras.queryParams));
        return true;
      });
    fixture = TestBed.createComponent(GameReviewsComponent);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  it('loads detail then own review and page one in parallel, focusing the title', () => {
    start();
    expect(el('[data-detail-skeleton]')).not.toBeNull();
    http.expectNone(mineUrl);
    http.expectNone((req) => req.url === listUrl);
    detail();
    expect(el('h1').textContent!.trim()).toBe('Reseñas de Portal 2');
    expect(document.activeElement).toBe(el('h1'));
    expect(el('img').getAttribute('src')).toBe(DETAIL.coverUrl);
    expect(el('[data-mine-skeleton]')).not.toBeNull();
    expect(el('[data-list-skeleton]')).not.toBeNull();
    const list = listRequest();
    expect(list.request.params.get('sort')).toBe('recent');
    expect(list.request.params.get('page')).toBe('1');
    expect(list.request.params.get('pageSize')).toBe('20');
    flushMine();
    list.flush({
      items: [PUBLIC],
      ratingSummary: DETAIL.ratingSummary,
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(el('[data-rating-summary]').textContent).toContain('4,3 · 12 reseñas');
    expect(el('[role="alert"]')).toBeNull();
    expect(el('[data-my-review]')).toBeNull();
  });

  it.each(['top', 'low', 'invalid'])(
    'restores sort %s from the URL or falls back to recent',
    (value) => {
      start(value);
      detail();
      flushMine();
      const req = listRequest();
      expect(req.request.params.get('sort')).toBe(value === 'invalid' ? 'recent' : value);
      req.flush({
        items: [],
        ratingSummary: DETAIL.ratingSummary,
        page: 1,
        pageSize: 20,
        hasMore: false,
      });
      fixture.detectChanges();
    },
  );

  it('resets pagination and cancels old requests when changing sort', () => {
    start();
    detail();
    flushMine();
    flushList([PUBLIC], { hasMore: true });
    click('load-more');
    const stale = listRequest();
    expect(stale.request.params.get('page')).toBe('2');
    sort('top');
    expect(stale.cancelled).toBe(true);
    expect(navigate).toHaveBeenCalledWith(
      [],
      expect.objectContaining({ queryParams: { sort: 'top' }, replaceUrl: true }),
    );
    expect(el('[data-review-id]')).toBeNull();
    const current = listRequest();
    expect(current.request.params.get('sort')).toBe('top');
    expect(current.request.params.get('page')).toBe('1');
    current.flush({
      items: [{ ...PUBLIC, id: 3 }],
      ratingSummary: DETAIL.ratingSummary,
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(el('[data-review-id="3"]')).not.toBeNull();
    expect(el('[data-review-id="2"]')).toBeNull();
  });

  it('uses the latest sort if it changes before detail arrives', () => {
    start();
    query.next(convertToParamMap({ sort: 'low' }));
    detail();
    flushMine();
    const req = listRequest();
    expect(req.request.params.get('sort')).toBe('low');
    req.flush({
      items: [],
      ratingSummary: DETAIL.ratingSummary,
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
  });

  it('appends unique reviews, prevents duplicate requests and hides load more at the end', () => {
    start();
    detail();
    flushMine();
    flushList([PUBLIC], { hasMore: true });
    click('load-more');
    expect(button('load-more').disabled).toBe(true);
    click('load-more');
    const req = listRequest();
    expect(req.request.params.get('page')).toBe('2');
    req.flush({
      items: [PUBLIC, { ...PUBLIC, id: 3 }],
      ratingSummary: DETAIL.ratingSummary,
      page: 2,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-review-id]')).toHaveLength(2);
    expect(button('load-more')).toBeNull();
  });

  it('shows mine once above the public list, in read-only mode', () => {
    start();
    detail();
    flushMine(MINE);
    flushList([{ ...PUBLIC, ...MINE, isMine: true }, PUBLIC]);
    expect(el('[data-my-review]').textContent).toContain('Mi opinión');
    expect(el('[data-my-review]').textContent).toContain('(editada)');
    expect(el('[data-my-review]').textContent).toContain('Podés editarla desde la ficha del juego');
    expect(el('[data-my-review] time').textContent).toBe('02/11/2026');
    expect(fixture.nativeElement.querySelectorAll('[data-review-id]')).toHaveLength(1);
    expect(el('[data-review-id="1"]')).toBeNull();
    expect(el('textarea')).toBeNull();
    expect(el('[data-action="edit-review"]')).toBeNull();
    expect(
      el('[data-my-review]').compareDocumentPosition(el('[data-review-id="2"]')) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('does not claim the game is unreviewed when mine is its only review', () => {
    start();
    detail();
    flushMine(MINE);
    flushList([{ ...PUBLIC, ...MINE, isMine: true }], { ratingSummary: { average: 4, count: 1 } });
    expect(el('[data-empty]').textContent).toContain('No hay reseñas de otros jugadores');
    expect(el('[data-rating-summary]').textContent).toContain('1 reseña');
  });

  it('shows a genuine empty state with a link to find the game', () => {
    start();
    detail();
    flushMine();
    flushList([], { ratingSummary: { average: null, count: 0 } });
    expect(el('[data-empty]').textContent).toContain('Todavía no hay reseñas de este juego');
    expect(el('[data-empty]').textContent).toContain('escribir la primera');
    expect(el('[data-empty] a').getAttribute('href')).toContain('/games?q=Portal%202');
  });

  it('renders untrusted text literally, preserves line breaks and expands long reviews', () => {
    start();
    detail();
    flushMine();
    const content = '<script>alert(1)</script>\n<b>hola</b>\n' + 'Largo '.repeat(90);
    flushList([{ ...PUBLIC, text: content, editedAt: '2026-11-02T12:00:00Z' }]);
    const p = el('[data-review-text]');
    expect(p.textContent).toBe(content);
    expect(p.classList.contains('whitespace-pre-line')).toBe(true);
    expect(p.classList.contains('line-clamp-5')).toBe(true);
    expect(p.querySelector('script, b')).toBeNull();
    expect(el('[data-review-id]').textContent).toContain('otro');
    expect(el('[data-review-id] time').textContent).toBe('02/11/2026');
    const expand = el<HTMLButtonElement>('[data-review-id] button');
    expand.click();
    fixture.detectChanges();
    expect(p.classList.contains('line-clamp-5')).toBe(false);
    expect(expand.textContent).toContain('Ver menos');
    expand.click();
    fixture.detectChanges();
    expect(p.classList.contains('line-clamp-5')).toBe(true);
  });

  it('shows stars without an empty text placeholder for rating-only reviews', () => {
    start();
    detail();
    flushMine();
    flushList([{ ...PUBLIC, text: null }]);
    expect(el('[data-review-id] app-review-content [role="img"]').getAttribute('aria-label')).toBe(
      'Puntuación 5 de 5',
    );
    expect(el('[data-review-text]')).toBeNull();
    expect(el('[data-review-id] button')).toBeNull();
  });

  it.each(['detail', 'mine', 'list'] as const)(
    'shows game_not_found from %s with a catalog link',
    (section) => {
      start();
      if (section !== 'detail') {
        detail();
        if (section === 'mine') flushList();
        else flushMine();
      }
      const req =
        section === 'detail'
          ? http.expectOne(detailUrl)
          : section === 'mine'
            ? http.expectOne(mineUrl)
            : listRequest();
      req.flush(
        { extensions: { code: 'game_not_found' } },
        { status: 404, statusText: 'Not Found' },
      );
      fixture.detectChanges();
      expect(el('[role="alert"]').textContent).toContain('No encontramos este juego');
      expect(el('[role="alert"] a').getAttribute('href')).toBe('/games');
    },
  );

  it.each(['catalog_unavailable', 'network'])('retries a detail failure: %s', (code) => {
    start();
    const req = http.expectOne(detailUrl);
    if (code === 'network') req.error(new ProgressEvent('error'));
    else req.flush({ extensions: { code } }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(el('[role="alert"]').textContent).toContain(
      code === 'network' ? 'Revisá tu conexión' : 'El catálogo no está disponible',
    );
    click('retry-detail');
    detail();
    flushMine();
    flushList();
  });

  it('retries my review independently without hiding public reviews', () => {
    start();
    detail();
    flushList();
    http.expectOne(mineUrl).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(el('[data-review-id]')).not.toBeNull();
    click('retry-mine');
    flushMine(MINE);
    expect(el('[data-my-review]')).not.toBeNull();
    http.expectNone((req) => req.url === listUrl);
  });

  it('keeps loaded pages and retries the failed next page', () => {
    start();
    detail();
    flushMine();
    flushList([PUBLIC], { hasMore: true });
    click('load-more');
    listRequest().error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(el('[data-review-id="2"]')).not.toBeNull();
    expect(el('[role="alert"]').textContent).toContain('Revisá tu conexión');
    click('retry-list');
    const req = listRequest();
    expect(req.request.params.get('page')).toBe('2');
    req.flush({
      items: [{ ...PUBLIC, id: 3 }],
      ratingSummary: DETAIL.ratingSummary,
      page: 2,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-review-id]')).toHaveLength(2);
  });

  it.each(['detail', 'mine', 'list'] as const)(
    'reuses the 429 countdown and requires an explicit retry for %s',
    (section) => {
      vi.useFakeTimers();
      start();
      if (section !== 'detail') {
        detail();
        if (section === 'mine') flushList();
        else flushMine();
      }
      const req =
        section === 'detail'
          ? http.expectOne(detailUrl)
          : section === 'mine'
            ? http.expectOne(mineUrl)
            : listRequest();
      req.flush(
        { extensions: { code: 'rate_limited' } },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
      );
      fixture.detectChanges();
      expect(el('[role="alert"]').textContent).toContain('2 segundos');
      expect(button(`retry-${section}`).disabled).toBe(true);
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();
      http.expectNone(() => true);
      expect(button(`retry-${section}`).disabled).toBe(false);
      click(`retry-${section}`);
      if (section === 'detail') {
        detail();
        flushMine();
        flushList();
      } else if (section === 'mine') flushMine();
      else flushList();
    },
  );

  it('cancels old game requests and never renders their data', () => {
    start();
    detail();
    const oldMine = http.expectOne(mineUrl);
    const oldList = listRequest();
    params.next(convertToParamMap({ externalId: '5000' }));
    fixture.detectChanges();
    expect(oldMine.cancelled).toBe(true);
    expect(oldList.cancelled).toBe(true);
    http
      .expectOne(`${environment.apiUrl}/games/5000`)
      .flush({ ...DETAIL, id: 18, externalId: 5000, name: 'Otro juego' });
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/games/18/reviews/me`)
      .flush({ ...MINE, gameId: 18, text: 'Otra reseña' });
    http
      .expectOne((req) => req.url === `${environment.apiUrl}/games/18/reviews`)
      .flush({
        items: [],
        ratingSummary: { average: 4, count: 1 },
        page: 1,
        pageSize: 20,
        hasMore: false,
      });
    fixture.detectChanges();
    expect(el('h1').textContent).toContain('Otro juego');
    expect(el('[data-my-review]').textContent).toContain('Otra reseña');
  });

  it('refreshes sorted pagination and my review after a service change', () => {
    ready();
    TestBed.inject(ReviewService).save(17, 5, 'Nueva opinión').subscribe();
    http.expectOne(mineUrl).flush({
      review: { ...MINE, text: 'Nueva opinión', rating: 5 },
      ratingSummary: { average: 4.5, count: 13 },
    });
    fixture.detectChanges();
    expect(el('[data-my-review]').textContent).toContain('Nueva opinión');
    expect(el('[data-rating-summary]').textContent).toContain('4,5');
    const req = listRequest();
    expect(req.request.params.get('page')).toBe('1');
    req.flush({
      items: [PUBLIC],
      ratingSummary: { average: 4.5, count: 13 },
      page: 1,
      pageSize: 20,
      hasMore: false,
    });
    fixture.detectChanges();
  });

  it('falls back to /games for a direct entry even after sorting', () => {
    ready();
    sort('low');
    flushList();
    click('back');
    expect(navigate).toHaveBeenLastCalledWith(['/games']);
  });
  it('keeps a retry available when changing games during my-review cooldown', () => {
    vi.useFakeTimers();
    start();
    detail();
    flushList();
    http
      .expectOne(mineUrl)
      .flush(
        { code: 'rate_limited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
      );
    params.next(convertToParamMap({ externalId: '5000' }));
    fixture.detectChanges();
    http
      .expectOne(`${environment.apiUrl}/games/5000`)
      .flush({ ...DETAIL, id: 18, externalId: 5000 });
    fixture.detectChanges();
    http
      .expectOne((req) => req.url === `${environment.apiUrl}/games/18/reviews`)
      .flush({
        items: [],
        ratingSummary: { average: null, count: 0 },
        page: 1,
        pageSize: 20,
        hasMore: false,
      });
    fixture.detectChanges();
    expect(button('retry-mine').disabled).toBe(true);
    http.expectNone(`${environment.apiUrl}/games/18/reviews/me`);
    vi.advanceTimersByTime(2000);
    fixture.detectChanges();
    click('retry-mine');
    http.expectOne(`${environment.apiUrl}/games/18/reviews/me`).flush({ ...MINE, gameId: 18 });
    fixture.detectChanges();
    expect(el('[data-my-review]')).not.toBeNull();
  });

  it('rejects invalid external IDs without HTTP', () => {
    params.next(convertToParamMap({ externalId: 'invalid' }));
    fixture.detectChanges();
    expect(el('[role="alert"]').textContent).toContain('No encontramos este juego');
    http.expectNone(() => true);
  });
});
