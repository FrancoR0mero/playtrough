import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TimelineEntryCardComponent } from '../timeline/timeline-entry-card';
import { feedEntry } from './feed-test-helpers';

describe('Feed activity card', () => {
  function setup(occurredOn = '2026-03-10') {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideRouter([])] });
    const fixture = TestBed.createComponent(TimelineEntryCardComponent);
    fixture.componentRef.setInput(
      'entry',
      feedEntry({
        occurredOn,
        type: 'reviewed',
        review: { id: 3, rating: 4.5, excerpt: '<script>literal</script>' },
      }),
    );
    fixture.componentRef.setInput('showAuthor', true);
    fixture.componentRef.setInput('perspective', 'other');
    fixture.componentRef.setInput('referenceTime', Date.parse('2026-03-12T14:00:00Z'));
    fixture.detectChanges();
    return { fixture, root: fixture.nativeElement as HTMLElement };
  }
  it('renders author, avatar, review and relative publication date with its full timestamp', () => {
    const f = setup();
    expect(f.root.querySelector('a[href="/profile/bea"]')?.textContent).toContain('bea');
    expect(f.root.querySelector('app-user-avatar')).not.toBeNull();
    expect(f.root.querySelector('[data-publication-date]')?.textContent).toContain('hace 2 horas');
    expect(f.root.querySelector('[data-publication-date]')?.getAttribute('datetime')).toBe(
      '2026-03-12T12:00:00.000Z',
    );
    expect(f.root.querySelector('[data-publication-date]')?.getAttribute('title')).toContain(
      '2026',
    );
    expect(f.root.querySelector('[data-effective-date]')?.textContent).toContain(
      'jugado el 10/03/2026',
    );
    expect(f.root.textContent).toContain('reseñó');
    expect(
      f.root.querySelector('app-star-rating [role="img"]')?.getAttribute('aria-label'),
    ).toContain('4,5');
    expect(f.root.querySelector('[data-review-excerpt]')?.textContent).toContain(
      '<script>literal</script>',
    );
    expect(f.root.querySelector('script, [data-timeline-menu]')).toBeNull();
  });
  it('omits the extra effective date when both UTC calendar dates match', () => {
    const f = setup('2026-03-12');
    expect(f.root.querySelector('[data-effective-date]')).toBeNull();
  });
  it('recomputes relative time when the parent clock changes', () => {
    const f = setup();
    f.fixture.componentRef.setInput('referenceTime', Date.parse('2026-03-12T15:00:00Z'));
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-publication-date]')?.textContent).toContain('hace 3 horas');
  });
});
