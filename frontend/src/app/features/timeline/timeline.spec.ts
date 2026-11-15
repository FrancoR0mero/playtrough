import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { LibraryService } from '../library/services/library';
import { ReviewService } from '../reviews/services/review';
import { TimelinePage } from './models/timeline';
import { TimelineService } from './services/timeline';
import { TimelineComponent } from './timeline';
import { timelineEntry } from './timeline-test-helpers';

describe('Own timeline', () => {
  function setup(page: TimelinePage = { items: [], nextCursor: null, hasMore: false }) {
    const library = new Subject();
    const reviews = new Subject();
    const service = { getMine: vi.fn().mockReturnValue(of(page)), getUser: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: TimelineService, useValue: service },
        { provide: LibraryService, useValue: { changes$: library } },
        { provide: ReviewService, useValue: { changes$: reviews } },
      ],
    });
    const fixture = TestBed.createComponent(TimelineComponent);
    return { fixture, root: fixture.nativeElement as HTMLElement, service, library, reviews };
  }
  afterEach(() => vi.useRealTimers());
  it('loads only once, groups by calendar month and announces results', () => {
    const f = setup({
      items: [
        timelineEntry(),
        timelineEntry({ id: 2, occurredOn: '2026-03-01' }),
        timelineEntry({ id: 3, occurredOn: '2026-02-28' }),
      ],
      nextCursor: null,
      hasMore: false,
    });
    f.fixture.detectChanges();
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledExactlyOnceWith({ limit: 20 });
    expect([...f.root.querySelectorAll('h2')].map((h) => h.textContent?.trim())).toEqual([
      'Marzo de 2026',
      'Febrero de 2026',
    ]);
    expect(f.root.querySelector('ol')?.getAttribute('aria-label')).toBe('Actividad de juegos');
    expect(f.root.querySelector('[aria-live="polite"]')?.textContent).toContain(
      '3 entradas disponibles',
    );
    expect(f.root.textContent).not.toContain('Cargar más');
  });
  it('paginates without duplicate entries or concurrent requests and groups across page boundaries', () => {
    const f = setup({ items: [timelineEntry()], nextCursor: 'v1.opaque', hasMore: true });
    f.fixture.detectChanges();
    const pending = new Subject<TimelinePage>();
    f.service.getMine.mockReturnValue(pending);
    const more = f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!;
    more.click();
    more.click();
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(2);
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20, cursor: 'v1.opaque' });
    expect(more.disabled).toBe(true);
    pending.next({
      items: [timelineEntry(), timelineEntry({ id: 2 })],
      nextCursor: null,
      hasMore: false,
    });
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(2);
    expect(f.root.querySelectorAll('h2')).toHaveLength(1);
    expect(f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')).toBeNull();
  });
  it('shows skeletons and then the own empty state with both destinations', () => {
    const f = setup();
    const pending = new Subject<TimelinePage>();
    f.service.getMine.mockReturnValue(pending);
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-timeline-skeleton]')).not.toBeNull();
    expect(f.root.textContent).not.toContain('Tu línea de tiempo está vacía');
    pending.next({ items: [], nextCursor: null, hasMore: false });
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-timeline-skeleton]')).toBeNull();
    expect(f.root.textContent).toContain('Tu línea de tiempo está vacía');
    expect(f.root.querySelector('a[href="/games"]')).not.toBeNull();
    expect(f.root.querySelector('a[href="/library"]')).not.toBeNull();
  });
  it('retries a failed first request and keeps loaded entries when a later page fails', () => {
    const f = setup();
    f.service.getMine.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('No pudimos cargar');
    f.service.getMine.mockReturnValue(
      of({ items: [timelineEntry()], nextCursor: 'next', hasMore: true }),
    );
    f.root.querySelector('button')!.click();
    f.fixture.detectChanges();
    f.service.getMine.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(1);
    f.service.getMine.mockReturnValue(
      of({ items: [timelineEntry({ id: 2 })], nextCursor: null, hasMore: false }),
    );
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20, cursor: 'next' });
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(2);
  });
  it('honors the shared 429 countdown without automatically retrying', () => {
    vi.useFakeTimers();
    const f = setup();
    f.service.getMine.mockReturnValueOnce(
      throwError(
        () =>
          new RateLimitError(
            new HttpErrorResponse({
              status: 429,
              headers: new HttpHeaders({ 'Retry-After': '2' }),
            }),
          ),
      ),
    );
    f.fixture.detectChanges();
    const retry = f.root.querySelector('button')!;
    expect(retry.disabled).toBe(true);
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('2');
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    expect(retry.disabled).toBe(false);
    expect(f.service.getMine).toHaveBeenCalledTimes(1);
    retry.click();
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(2);
    f.fixture.destroy();
  });
  it.each(['library', 'reviews'] as const)(
    'reloads on next show after %s changes, not during reading',
    (source) => {
      const f = setup({ items: [timelineEntry()], nextCursor: 'old', hasMore: true });
      f.fixture.detectChanges();
      f[source].next({});
      f.fixture.detectChanges();
      expect(f.service.getMine).toHaveBeenCalledTimes(1);
      f.fixture.componentRef.setInput('active', false);
      f.fixture.detectChanges();
      f.fixture.componentRef.setInput('active', true);
      f.fixture.detectChanges();
      expect(f.service.getMine).toHaveBeenCalledTimes(2);
      expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
    },
  );
  it('does not fetch while inactive and reuses an unchanged list when shown again', () => {
    const f = setup();
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    expect(f.service.getMine).not.toHaveBeenCalled();
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(1);
  });
  it('cancels a pending request on hide and does not lose changes arriving during a request', () => {
    const f = setup();
    const pending = new Subject<TimelinePage>();
    f.service.getMine.mockReturnValueOnce(pending);
    f.fixture.detectChanges();
    f.library.next({});
    pending.next({ items: [], nextCursor: null, hasMore: false });
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    f.service.getMine.mockReturnValueOnce(pending);
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(2);
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    expect(pending.observed).toBe(false);
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(3);
  });
  it('restarts from the first page after invalid_cursor', () => {
    const f = setup({ items: [timelineEntry()], nextCursor: 'old', hasMore: true });
    f.fixture.detectChanges();
    f.service.getMine.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({ status: 400, error: { extensions: { code: 'invalid_cursor' } } }),
      ),
    );
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('ya no es válida');
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
  });

  it('keeps an invalidation when another page arrives before the tab is reopened', () => {
    const f = setup({ items: [timelineEntry()], nextCursor: 'next', hasMore: true });
    f.fixture.detectChanges();
    f.library.next({});
    f.service.getMine.mockReturnValueOnce(
      of({ items: [timelineEntry({ id: 2 })], nextCursor: null, hasMore: false }),
    );
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    expect(f.service.getMine).toHaveBeenCalledTimes(3);
    expect(f.service.getMine).toHaveBeenLastCalledWith({ limit: 20 });
  });
});
