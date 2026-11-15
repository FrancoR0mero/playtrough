import { TestBed } from '@angular/core/testing';
import { TimelineFilterPanelComponent } from './timeline-filter-panel';
import { TimelineFilters } from './timeline-filters';

describe('Timeline filter controls', () => {
  function setup(filters: TimelineFilters = {}) {
    const fixture = TestBed.createComponent(TimelineFilterPanelComponent);
    fixture.componentRef.setInput('filters', filters);
    fixture.componentRef.setInput('periods', {
      years: [
        {
          year: 2026,
          count: 5,
          months: [
            { month: 3, count: 2 },
            { month: 2, count: 3 },
          ],
        },
        { year: 2025, count: 1, months: [{ month: 12, count: 1 }] },
      ],
    });
    const changed = vi.fn();
    fixture.componentInstance.changed.subscribe(changed);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const select = (key: string, value: string) => {
      const el = root.querySelector<HTMLSelectElement>(`[data-filter-${key}]`)!;
      el.value = value;
      el.dispatchEvent(new Event('change'));
      fixture.detectChanges();
    };
    return { fixture, root, changed, select };
  }
  it('shows period counts and disables month until a year is selected', () => {
    const f = setup();
    expect(f.root.querySelector('[data-filter-year]')?.textContent).toContain('2026 (5)');
    expect(f.root.querySelector<HTMLSelectElement>('[data-filter-month]')!.disabled).toBe(true);
    f.select('year', '2026');
    expect(f.changed).toHaveBeenLastCalledWith({ year: 2026, month: undefined });
    f.fixture.componentRef.setInput('filters', { year: 2026 });
    f.fixture.detectChanges();
    expect(f.root.querySelector<HTMLSelectElement>('[data-filter-month]')!.disabled).toBe(false);
    expect(f.root.querySelector('[data-filter-month]')?.textContent).toContain('marzo (2)');
    f.select('month', '3');
    expect(f.changed).toHaveBeenLastCalledWith({ year: 2026, month: 3 });
  });
  it('clears month when the year changes and preserves types', () => {
    const f = setup({ year: 2026, month: 3, type: ['reviewed'] });
    f.select('year', '2025');
    expect(f.changed).toHaveBeenLastCalledWith({
      year: 2025,
      month: undefined,
      type: ['reviewed'],
    });
  });
  it('supports multiple type chips, deselection, and clearing all filters', () => {
    const f = setup({ type: ['reviewed'] });
    f.root.querySelector<HTMLButtonElement>('[data-filter-type="added"]')!.click();
    expect(f.changed).toHaveBeenLastCalledWith({ type: ['added', 'reviewed'] });
    f.fixture.componentRef.setInput('filters', { type: ['added', 'reviewed'] });
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-filter-type="added"]')?.getAttribute('aria-pressed')).toBe(
      'true',
    );
    f.root.querySelector<HTMLButtonElement>('[data-filter-type="reviewed"]')!.click();
    expect(f.changed).toHaveBeenLastCalledWith({ type: ['added'] });
    f.root.querySelector<HTMLButtonElement>('[data-clear-filters]')!.click();
    expect(f.changed).toHaveBeenLastCalledWith({});
  });
  it('keeps URL-restored selections visible even if the periods have no matching counts', () => {
    const f = setup({ year: 2024, month: 1 });
    expect(f.root.querySelector<HTMLSelectElement>('[data-filter-year]')!.value).toBe('2024');
    expect(f.root.querySelector<HTMLSelectElement>('[data-filter-month]')!.value).toBe('1');
    expect(f.root.querySelector('[data-filter-year]')?.textContent).toContain('2024 (0)');
  });
  it('provides a collapsible mobile panel without issuing changes on initialization', () => {
    const f = setup();
    const button = f.root.querySelector<HTMLButtonElement>('[aria-expanded]')!;
    expect(button.getAttribute('aria-expanded')).toBe('false');
    button.click();
    f.fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(f.changed).not.toHaveBeenCalled();
  });
});
