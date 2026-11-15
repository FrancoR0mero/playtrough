import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { throwError } from 'rxjs';
import { GameCatalogService } from '../games/services/game-catalog';
import { TimelineEntryCardComponent } from './timeline-entry-card';
import { TIMELINE_IMAGE_ASPECT_RATIO } from './timeline-text';
import { timelineEntry } from './timeline-test-helpers';

describe('Timeline entry card', () => {
  function setup(entry = timelineEntry()) {
    const catalog = {
      getLocalDetail: vi
        .fn()
        .mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 }))),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: GameCatalogService, useValue: catalog },
      ],
    });
    const fixture = TestBed.createComponent(TimelineEntryCardComponent);
    fixture.componentRef.setInput('entry', entry);
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement, catalog };
  }
  it.each([
    ['added', 'Empezaste a jugar a'],
    ['status_changed', 'Empezaste a jugar a'],
    ['reviewed', 'Reseñaste'],
  ] as const)('renders %s with a game button and calendar date', (type, text) => {
    const f = setup(timelineEntry({ type }));
    expect(f.root.textContent).toContain(text);
    expect(f.root.querySelector('button')?.textContent?.trim()).toBe('Portal 2');
    expect(f.root.querySelector('time')?.getAttribute('datetime')).toBe('2026-03-12');
    expect(f.root.querySelector('time')?.textContent?.trim()).toBe('12 de marzo de 2026');
    expect(f.root.querySelector<HTMLElement>('[style]')?.style.aspectRatio).toBe(
      TIMELINE_IMAGE_ASPECT_RATIO,
    );
  });
  it('shows a placeholder for a missing or failed image', () => {
    const f = setup();
    expect(f.root.querySelector('[data-placeholder]')).not.toBeNull();
    f.fixture.componentRef.setInput(
      'entry',
      timelineEntry({
        game: { ...timelineEntry().game, coverUrl: 'https://example.test/game.webp' },
      }),
    );
    f.fixture.detectChanges();
    const img = f.root.querySelector('img')!;
    expect(img.className).toContain('object-cover');
    img.dispatchEvent(new Event('error'));
    f.fixture.detectChanges();
    expect(f.root.querySelector('img')).toBeNull();
    expect(f.root.querySelector('[data-placeholder]')).not.toBeNull();
  });
  it.each([
    null,
    'Una reseña\ncon dos líneas',
    '<img src=x onerror=alert(1)><script>bad()</script>',
  ])('renders review excerpt safely: %s', (excerpt) => {
    const f = setup(timelineEntry({ type: 'reviewed', review: { id: 3, rating: 4.5, excerpt } }));
    expect(f.root.querySelector('app-star-rating [role="img"]')?.getAttribute('aria-label')).toBe(
      'Puntuación 4,5 de 5',
    );
    expect(f.root.querySelector('[data-review-excerpt]')?.textContent?.trim() ?? null).toBe(
      excerpt,
    );
    expect(f.root.querySelector('script, [onerror]')).toBeNull();
    expect(f.root.querySelector('a')?.getAttribute('href')).toBe('/games/4200/reviews');
  });
  it('marks hidden entries only for their owner, without edit actions in this phase', () => {
    const f = setup(timelineEntry({ isHidden: true }));
    expect(f.root.querySelector('[data-hidden]')?.textContent).toContain('Oculta para otros');
    f.fixture.componentRef.setInput('perspective', 'other');
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-hidden]')).toBeNull();
    expect(f.root.querySelector('[aria-haspopup="menu"]')).toBeNull();
  });
  it('opens one real detail dialog and restores focus to the game button on close', async () => {
    const f = setup();
    const button = f.root.querySelector('button')!;
    button.focus();
    button.click();
    button.click();
    f.fixture.detectChanges();
    await f.fixture.whenStable();
    const dialogs = TestBed.inject(Dialog);
    expect(dialogs.openDialogs).toHaveLength(1);
    expect(dialogs.openDialogs[0].config.data.externalId).toBe(4200);
    expect(f.catalog.getLocalDetail).toHaveBeenCalledWith(17);
    document.querySelector<HTMLButtonElement>('[data-action="close"]')!.click();
    await f.fixture.whenStable();
    expect(dialogs.openDialogs).toHaveLength(0);
    expect(document.activeElement).toBe(button);
  });
});
