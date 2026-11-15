import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { TimelineService } from './services/timeline';
import { TimelineEntryCardComponent } from './timeline-entry-card';
import { timelineEntry } from './timeline-test-helpers';
import { TimelineEntryDto } from './models/timeline';

describe('Timeline visibility actions', () => {
  function setup(hidden = false) {
    const entry = timelineEntry({
      isHidden: hidden,
      type: 'reviewed',
      review: { id: 3, rating: 4, excerpt: 'Conservar esta reseña' },
    });
    const service = { setVisibility: vi.fn().mockReturnValue(of({ ...entry, isHidden: !hidden })) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: TimelineService, useValue: service },
      ],
    });
    const fixture = TestBed.createComponent(TimelineEntryCardComponent);
    fixture.componentRef.setInput('entry', entry);
    fixture.componentRef.setInput('showActions', true);
    const updated = vi.fn((value: TimelineEntryDto) =>
      fixture.componentRef.setInput('entry', value),
    );
    const missing = vi.fn();
    fixture.componentInstance.updated.subscribe(updated);
    fixture.componentInstance.missing.subscribe(missing);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const trigger = () => root.querySelector<HTMLButtonElement>('[data-timeline-menu]')!;
    const choose = () => {
      trigger().click();
      fixture.detectChanges();
      root.querySelector<HTMLButtonElement>('[role="menuitem"]')!.click();
      fixture.detectChanges();
    };
    return { fixture, root, service, updated, missing, trigger, choose };
  }
  afterEach(() => vi.useRealTimers());
  it('operates the menu with arrows and Escape and restores trigger focus', () => {
    const f = setup();
    f.trigger().focus();
    f.trigger().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }),
    );
    f.fixture.detectChanges();
    const item = f.root.querySelector<HTMLButtonElement>('[role="menuitem"]')!;
    expect(document.activeElement).toBe(item);
    expect(item.textContent).toContain('Ocultar a otros');
    for (const key of ['ArrowUp', 'ArrowDown', 'Home', 'End']) {
      item.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      expect(document.activeElement).toBe(item);
    }
    item.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(f.trigger());
  });
  it.each([false, true])(
    'updates hidden=%s from the server without changing the review',
    (hidden) => {
      const f = setup(hidden);
      f.choose();
      expect(f.service.setVisibility).toHaveBeenCalledExactlyOnceWith(1, !hidden);
      expect(f.updated).toHaveBeenCalledOnce();
      expect(!!f.root.querySelector('[data-hidden]')).toBe(!hidden);
      expect(f.root.textContent).toContain('Conservar esta reseña');
      expect(f.root.querySelector('[role="status"]')?.textContent).toContain(
        hidden ? 'visible' : 'oculta',
      );
    },
  );
  it('disables the entry and prevents duplicate writes while saving', () => {
    const f = setup();
    const pending = new Subject<TimelineEntryDto>();
    f.service.setVisibility.mockReturnValue(pending);
    f.choose();
    expect(f.trigger().disabled).toBe(true);
    expect(f.root.querySelector<HTMLButtonElement>('[aria-haspopup="dialog"]')!.disabled).toBe(
      true,
    );
    expect(f.root.querySelector('a')?.getAttribute('aria-disabled')).toBe('true');
    f.trigger().click();
    expect(f.service.setVisibility).toHaveBeenCalledOnce();
    pending.next(timelineEntry({ isHidden: true }));
    f.fixture.detectChanges();
    expect(f.trigger().disabled).toBe(false);
  });
  it('reports a missing entry for removal by the list', () => {
    const f = setup();
    f.service.setVisibility.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 404,
            error: { extensions: { code: 'entry_not_found' } },
          }),
      ),
    );
    f.choose();
    expect(f.missing).toHaveBeenCalledExactlyOnceWith(1);
    expect(f.updated).not.toHaveBeenCalled();
  });
  it.each([0, 400, 503])('keeps the entry and shows a recoverable error for %s', (status) => {
    const f = setup();
    f.service.setVisibility.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status })),
    );
    f.choose();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('No pudimos cambiar');
    expect(f.missing).not.toHaveBeenCalled();
    f.choose();
    expect(f.updated).toHaveBeenCalledOnce();
  });
  it('honors a 429 countdown without retrying automatically', () => {
    vi.useFakeTimers();
    const f = setup();
    f.service.setVisibility.mockReturnValueOnce(
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
    f.choose();
    expect(f.trigger().disabled).toBe(true);
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('2');
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    expect(f.trigger().disabled).toBe(false);
    expect(f.service.setVisibility).toHaveBeenCalledOnce();
    f.fixture.destroy();
  });
  it('never renders owner actions on an other-player card', () => {
    const f = setup();
    f.fixture.componentRef.setInput('perspective', 'other');
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-timeline-menu]')).toBeNull();
  });
});
