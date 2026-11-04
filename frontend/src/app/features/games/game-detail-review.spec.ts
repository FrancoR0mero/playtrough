import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { LibraryService } from '../library/services/library';
import { MyReviewDto, RatingSummary } from '../reviews/models/review';
import { ReviewService } from '../reviews/services/review';
import { GameDetailComponent } from './game-detail';
import { GameDetail } from './models/game';

const DETAIL: GameDetail = {
  id: 17,
  externalId: 4200,
  name: 'Portal 2',
  slug: 'portal-2',
  summary: 'Puzles',
  coverUrl: null,
  coverUrlLarge: null,
  releaseDate: null,
  platforms: [],
  genres: [],
  developers: [],
  ratingSummary: { average: 4.3, count: 12 },
};
const REVIEW: MyReviewDto = {
  id: 8,
  gameId: 17,
  rating: 4,
  text: 'Muy bueno\nCon amigos',
  createdAt: '2026-11-01T12:00:00Z',
  editedAt: null,
};

describe('Own review inside the game detail modal', () => {
  let fixture: ComponentFixture<GameDetailComponent>;
  let http: HttpTestingController;
  let dialog: { close: ReturnType<typeof vi.fn>; disableClose: boolean };
  const detailUrl = `${environment.apiUrl}/games/4200`;
  const mineUrl = `${environment.apiUrl}/games/17/reviews/me`;
  const el = <T extends HTMLElement = HTMLElement>(selector: string) =>
    fixture.nativeElement.querySelector(selector) as T;
  const button = (action: string) => el<HTMLButtonElement>(`[data-action="${action}"]`);
  function click(action: string) {
    button(action).click();
    fixture.detectChanges();
  }
  function text(value: string) {
    const area = el<HTMLTextAreaElement>('textarea');
    area.value = value;
    area.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }
  function choose(rating = 4) {
    el<HTMLButtonElement>(`[role="radio"][aria-label="${rating} de 5 estrellas"]`).click();
    fixture.detectChanges();
  }
  function escape() {
    el('h2').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();
  }
  function start(review: MyReviewDto | null = null, summary = DETAIL.ratingSummary) {
    http.expectOne(detailUrl).flush({ ...DETAIL, ratingSummary: summary });
    fixture.detectChanges();
    const req = http.expectOne(mineUrl);
    if (review) req.flush(review);
    else
      req.flush(
        { extensions: { code: 'review_not_found' } },
        { status: 404, statusText: 'Not Found' },
      );
    fixture.detectChanges();
  }
  function saveResponse(review = REVIEW, ratingSummary: RatingSummary = { average: 4, count: 13 }) {
    http.expectOne(mineUrl).flush({ review, ratingSummary });
    fixture.detectChanges();
  }

  beforeEach(() => {
    dialog = { close: vi.fn(), disableClose: false };
    TestBed.configureTestingModule({
      imports: [GameDetailComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: DIALOG_DATA, useValue: DETAIL },
        { provide: DialogRef, useValue: dialog },
        {
          provide: LibraryService,
          useValue: {
            getEntry: () =>
              throwError(
                () =>
                  new HttpErrorResponse({
                    status: 404,
                    error: { extensions: { code: 'not_in_library' } },
                  }),
              ),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GameDetailComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
      vi.useRealTimers();
    }
  });

  it.each([
    { average: null, count: 0 },
    { average: 4, count: 1 },
    { average: 4.3, count: 12 },
  ])('renders summary %j', (summary) => {
    start(null, summary);
    const header = el('[data-rating-summary]');
    expect(header.textContent).toContain(
      summary.count
        ? `${summary.count} ${summary.count === 1 ? 'reseña' : 'reseñas'}`
        : 'Sin reseñas todavía',
    );
    if (summary.average)
      expect(header.textContent).toContain(summary.average.toFixed(1).replace('.', ','));
  });

  it('shows a skeleton, queries the local ID and treats review_not_found as normal', () => {
    http.expectOne(detailUrl).flush(DETAIL);
    fixture.detectChanges();
    expect(el('[data-review-skeleton]')).not.toBeNull();
    const req = http.expectOne(mineUrl);
    expect(req.request.method).toBe('GET');
    req.flush(
      { extensions: { code: 'review_not_found' } },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();
    expect(button('write-review')).not.toBeNull();
    expect(el('[role="alert"]')).toBeNull();
  });

  it('shows edited date, rating and expandable plain text', () => {
    const value = '<b>hola</b>\n' + 'a'.repeat(410);
    start({ ...REVIEW, text: value, editedAt: '2026-11-02T12:00:00Z' });
    expect(el('app-my-review').textContent).toContain('(editada)');
    expect(el('app-my-review time').textContent).toBe('02/11/2026');
    expect(el('app-my-review [role="img"]').getAttribute('aria-label')).toBe('Puntuación 4 de 5');
    expect(el('#my-review-text').textContent).toContain('<b>hola</b>');
    expect(el('#my-review-text b')).toBeNull();
    expect(el('#my-review-text').textContent!.length).toBe(401);
    click('expand-review');
    expect(el('#my-review-text').textContent).toBe(value);
    expect(el('#my-review-text').classList.contains('whitespace-pre-line')).toBe(true);
    click('expand-review');
    expect(el('#my-review-text').textContent!.length).toBe(401);
  });

  it('requires a rating and counts Unicode scalars after backend normalization', () => {
    start();
    click('write-review');
    expect(button('save-review').disabled).toBe(true);
    expect(el('#review-counter').textContent).toContain('0 / 2.000');
    text('  😀\r\nHola  ');
    expect(el('#review-counter').textContent).toContain('6 / 2.000');
    expect(button('save-review').disabled).toBe(true);
    choose();
    text('😀'.repeat(2000));
    expect(el('#review-counter').textContent).toContain('2.000 / 2.000');
    expect(button('save-review').disabled).toBe(false);
    text('😀'.repeat(2001));
    expect(button('save-review').disabled).toBe(true);
    expect(el('textarea').getAttribute('aria-invalid')).toBe('true');
    expect(el('#review-counter').textContent).toContain('Límite excedido');
  });

  it('prefills editing, disables unchanged data and returns focus on cancel', () => {
    start(REVIEW);
    click('edit-review');
    expect(el<HTMLTextAreaElement>('textarea').value).toBe(REVIEW.text);
    expect(el('[aria-checked="true"]').getAttribute('aria-label')).toBe('4 de 5 estrellas');
    expect(button('save-review').disabled).toBe(true);
    expect(document.activeElement).toBe(el('h2'));
    expect(el('h2').textContent).toContain('Tu reseña de Portal 2');
    expect(dialog.disableClose).toBe(true);
    text('Otro texto');
    expect(button('save-review').disabled).toBe(false);
    text(REVIEW.text!);
    expect(button('save-review').disabled).toBe(true);
    click('cancel-review');
    expect(el('textarea')).toBeNull();
    expect(document.activeElement).toBe(button('edit-review'));
    expect(dialog.disableClose).toBe(false);
  });

  it('preserves a fractional backend rating when editing text and keeps a keyboard entry point', () => {
    start({ ...REVIEW, rating: 3.5 });
    click('edit-review');
    expect(el('[role="radio"][tabindex="0"]')).not.toBeNull();
    text('Actualizada');
    click('save-review');
    const req = http.expectOne(mineUrl);
    expect(req.request.body).toEqual({ rating: 3.5, text: 'Actualizada' });
    req.flush({
      review: { ...REVIEW, rating: 3.5, text: 'Actualizada' },
      ratingSummary: DETAIL.ratingSummary,
    });
    fixture.detectChanges();
  });

  it.each([false, true])(
    'saves creation/edit=%s, blocks duplicate actions and updates the header',
    (editing) => {
      start(editing ? REVIEW : null);
      click(editing ? 'edit-review' : 'write-review');
      choose(5);
      text('  Excelente\nJuego  ');
      const changed = vi.fn();
      TestBed.inject(ReviewService).changes$.subscribe(changed);
      click('save-review');
      expect(el<HTMLTextAreaElement>('textarea').disabled).toBe(true);
      expect(button('save-review').disabled).toBe(true);
      expect(button('close').disabled).toBe(true);
      escape();
      expect(button('discard-review')).toBeNull();
      expect(dialog.close).not.toHaveBeenCalled();
      const req = http.expectOne(mineUrl);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ rating: 5, text: 'Excelente\nJuego' });
      req.flush(
        {
          review: { ...REVIEW, rating: 5, text: 'Excelente\nJuego' },
          ratingSummary: { average: 4.4, count: 13 },
        },
        { status: editing ? 200 : 201, statusText: 'OK' },
      );
      fixture.detectChanges();
      expect(el('textarea')).toBeNull();
      expect(el('app-my-review [role="status"]').textContent).toContain(
        editing ? 'Reseña actualizada' : 'Reseña publicada',
      );
      expect(el('[data-rating-summary]').textContent).toContain('4,4 · 13 reseñas');
      expect(changed).toHaveBeenCalledOnce();
      expect(document.activeElement).toBe(button('edit-review'));
    },
  );

  it.each(['close', 'cancel-review', 'escape'])(
    'confirms discard through %s and can keep editing',
    (action) => {
      start();
      click('write-review');
      text('No perder');
      if (action === 'escape') escape();
      else click(action);
      expect(el('app-my-review').textContent).toContain('¿Descartar los cambios?');
      expect(document.activeElement).toBe(button('keep-review'));
      expect(dialog.close).not.toHaveBeenCalled();
      click('keep-review');
      expect(el<HTMLTextAreaElement>('textarea').value).toBe('No perder');
      click('cancel-review');
      click('discard-review');
      expect(el('textarea')).toBeNull();
      expect(document.activeElement).toBe(button('write-review'));
    },
  );

  it.each(['close', 'cancel-review', 'escape'])(
    'returns directly without changes through %s',
    (action) => {
      start();
      click('write-review');
      if (action === 'escape') escape();
      else click(action);
      expect(el('textarea')).toBeNull();
      expect(button('discard-review')).toBeNull();
      expect(dialog.close).not.toHaveBeenCalled();
      expect(document.activeElement).toBe(button('write-review'));
    },
  );

  it.each([
    [400, 'invalid_rating', 'Elegí una puntuación'],
    [400, 'invalid_text', '2.000 caracteres'],
    [404, 'game_not_found', 'No encontramos este juego'],
    [409, 'concurrent_modification', 'Podés reintentar'],
    [500, 'unknown', 'Revisá tu conexión'],
  ] as const)('preserves input on %s %s and permits retry', (status, code, message) => {
    start();
    click('write-review');
    choose();
    text('Conservar');
    click('save-review');
    http.expectOne(mineUrl).flush({ extensions: { code } }, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(el('app-my-review [role="alert"]').textContent).toContain(message);
    expect(el<HTMLTextAreaElement>('textarea').value).toBe('Conservar');
    expect(button('save-review').disabled).toBe(false);
    click('save-review');
    saveResponse();
  });

  it('keeps input on a network failure', () => {
    start();
    click('write-review');
    choose();
    text('Sin conexión');
    click('save-review');
    http.expectOne(mineUrl).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(el('[role="alert"]').textContent).toContain('Revisá tu conexión');
    expect(el<HTMLTextAreaElement>('textarea').value).toBe('Sin conexión');
  });

  it.each(['load', 'save', 'delete'])(
    'reuses Retry-After for %s with an explicit retry',
    (operation) => {
      vi.useFakeTimers();
      if (operation === 'load') {
        http.expectOne(detailUrl).flush(DETAIL);
        fixture.detectChanges();
      } else {
        start(operation === 'delete' ? REVIEW : null);
        if (operation === 'save') {
          click('write-review');
          choose();
          text('Conservar');
          click('save-review');
        } else {
          click('remove-review');
          click('confirm-remove-review');
        }
      }
      http
        .expectOne(mineUrl)
        .flush(
          { extensions: { code: 'rate_limited' } },
          { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
        );
      fixture.detectChanges();
      const action =
        operation === 'load'
          ? 'retry-review'
          : operation === 'save'
            ? 'save-review'
            : 'confirm-remove-review';
      expect(el('[role="alert"]').textContent).toContain('2 segundos');
      expect(button(action).disabled).toBe(true);
      vi.advanceTimersByTime(2000);
      fixture.detectChanges();
      expect(button(action).disabled).toBe(false);
      http.expectNone(mineUrl);
      click(action);
      if (operation === 'save') saveResponse();
      else {
        http
          .expectOne(mineUrl)
          .flush(operation === 'load' ? REVIEW : { ratingSummary: { average: null, count: 0 } });
        fixture.detectChanges();
      }
    },
  );

  it('retries lookup failures and does not interpret unrelated 404s as no review', () => {
    http.expectOne(detailUrl).flush(DETAIL);
    fixture.detectChanges();
    http
      .expectOne(mineUrl)
      .flush({ extensions: { code: 'game_not_found' } }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(button('write-review')).toBeNull();
    expect(el('[role="alert"]').textContent).toContain('No encontramos');
    click('retry-review');
    http.expectOne(mineUrl).flush(REVIEW);
    fixture.detectChanges();
    expect(button('edit-review')).not.toBeNull();
  });

  it('cancels deletion without a request, then deletes and updates the summary', () => {
    start(REVIEW);
    click('remove-review');
    expect(el('app-my-review').textContent).toContain('¿Eliminar tu reseña?');
    click('cancel-remove-review');
    http.expectNone(mineUrl);
    expect(document.activeElement).toBe(button('remove-review'));
    click('remove-review');
    click('confirm-remove-review');
    const req = http.expectOne(mineUrl);
    expect(req.request.method).toBe('DELETE');
    req.flush({ ratingSummary: { average: null, count: 0 } });
    fixture.detectChanges();
    expect(button('write-review')).not.toBeNull();
    expect(el('[data-rating-summary]').textContent).toContain('Sin reseñas todavía');
    expect(el('app-my-review [role="status"]').textContent).toContain('Reseña eliminada');
  });

  it('treats an already deleted review as success and reloads the authoritative summary', () => {
    start(REVIEW);
    click('remove-review');
    click('confirm-remove-review');
    http
      .expectOne(mineUrl)
      .flush(
        { extensions: { code: 'review_not_found' } },
        { status: 404, statusText: 'Not Found' },
      );
    fixture.detectChanges();
    expect(button('write-review')).not.toBeNull();
    expect(el('[role="alert"]')).toBeNull();
    http.expectOne(detailUrl).flush({ ...DETAIL, ratingSummary: { average: 3, count: 11 } });
    fixture.detectChanges();
    expect(el('[data-rating-summary]').textContent).toContain('3,0 · 11 reseñas');
  });

  it('keeps the review and confirmation on a failed delete', () => {
    start(REVIEW);
    click('remove-review');
    click('confirm-remove-review');
    http.expectOne(mineUrl).error(new ProgressEvent('error'));
    fixture.detectChanges();
    expect(el('#my-review-text').textContent).toContain(REVIEW.text);
    expect(el('[role="alert"]').textContent).toContain('Revisá tu conexión');
    expect(button('confirm-remove-review').disabled).toBe(false);
  });

  it('updates from service events for this game only', () => {
    start(REVIEW);
    const service = TestBed.inject(ReviewService);
    service.save(99, 5).subscribe();
    http
      .expectOne(`${environment.apiUrl}/games/99/reviews/me`)
      .flush({ review: { ...REVIEW, gameId: 99 }, ratingSummary: { average: 5, count: 99 } });
    fixture.detectChanges();
    expect(el('[data-rating-summary]').textContent).toContain('12 reseñas');
    service.save(17, 5).subscribe();
    saveResponse({ ...REVIEW, rating: 5 }, { average: 4.5, count: 12 });
    expect(el('[data-rating-summary]').textContent).toContain('4,5');
    expect(el('app-my-review [role="img"]').getAttribute('aria-label')).toBe('Puntuación 5 de 5');
  });
});
