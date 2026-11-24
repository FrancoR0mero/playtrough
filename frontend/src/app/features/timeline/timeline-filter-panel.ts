import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { TimelinePeriods, TimelineType } from './models/timeline';
import { TIMELINE_TYPES, TimelineFilters, hasTimelineFilters } from './timeline-filters';
import { TIMELINE_TEXT } from './timeline-text';

const TYPE_GROUPS = {
  library: ['added', 'status_changed'],
  reviewed: ['reviewed'],
} satisfies Record<string, TimelineType[]>;
type TypeGroup = keyof typeof TYPE_GROUPS;

@Component({
  selector: 'app-timeline-filter-panel',
  imports: [ReactiveFormsModule],
  template: `
    <section [attr.aria-label]="text.filters" class="mb-6 border-y border-slate-700 py-4">
      <p class="hidden text-base font-semibold text-white sm:block">{{ text.filters }}</p>
      <button
        type="button"
        (click)="expanded.set(!expanded())"
        [attr.aria-expanded]="expanded()"
        aria-controls="timeline-filter-controls"
        class="flex min-h-11 w-full items-center justify-between text-left font-semibold text-white sm:hidden"
      >
        {{ text.filters }}
        <svg
          aria-hidden="true"
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.8"
          [class.rotate-180]="expanded()"
        >
          <path d="m6 9 6 6 6-6" />
        </svg>
      </button>
      <div
        id="timeline-filter-controls"
        [class.hidden]="!expanded()"
        class="mt-3 space-y-4 sm:block"
      >
        <div class="grid gap-4 sm:grid-cols-2">
          <label class="block text-sm text-slate-300"
            ><span>{{ text.year }}</span>
            <select
              [formControl]="year"
              data-filter-year
              class="mt-2 min-h-11 w-full rounded-lg border border-slate-600 bg-slate-900 px-3 text-white"
            >
              <option value="">{{ text.allYears }}</option>
              @if (filters().year && !selectedYear()) {
                <option [value]="filters().year">{{ filters().year }} (0)</option>
              }
              @for (period of periods().years; track period.year) {
                <option [value]="period.year">{{ period.year }} ({{ period.count }})</option>
              }
            </select>
          </label>
          <label class="block text-sm text-slate-300"
            ><span>{{ text.month }}</span>
            <select
              [formControl]="month"
              data-filter-month
              class="mt-2 min-h-11 w-full rounded-lg border border-slate-600 bg-slate-900 px-3 text-white"
            >
              <option value="">{{ text.allMonths }}</option>
              @for (period of months(); track period.month) {
                <option [value]="period.month">
                  {{ monthName(period.month) }} ({{ period.count }})
                </option>
              }
            </select>
          </label>
        </div>
        <fieldset>
          <legend class="mb-2 text-sm text-slate-300">{{ text.types }}</legend>
          <div class="flex flex-wrap gap-2">
            @for (type of types; track type) {
              <button
                type="button"
                [attr.aria-pressed]="typeState(type)"
                (click)="toggleType(type)"
                [attr.data-filter-type]="type"
                class="ui-button min-h-11 border px-4 py-2 text-sm text-white"
                [class.border-violet-300]="typeState(type) !== false"
                [class.bg-violet-400/15]="typeState(type) === true"
                [class.border-slate-600]="typeState(type) === false"
              >
                {{ text.typeLabels[type] }}
              </button>
            }
          </div>
        </fieldset>
        <button
          type="button"
          (click)="changed.emit({})"
          [disabled]="!hasFilters(filters())"
          data-clear-filters
          class="min-h-11 px-1 text-sm text-violet-300 underline underline-offset-4 disabled:text-slate-400 disabled:no-underline"
        >
          {{ text.clearFilters }}
        </button>
      </div>
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineFilterPanelComponent {
  readonly filters = input<TimelineFilters>({});
  readonly periods = input<TimelinePeriods>({ years: [] });
  readonly changed = output<TimelineFilters>();
  protected readonly text = TIMELINE_TEXT;
  protected readonly types: readonly TypeGroup[] = ['library', 'reviewed'];
  protected readonly expanded = signal(false);
  protected readonly year = new FormControl('', { nonNullable: true });
  protected readonly month = new FormControl('', { nonNullable: true });
  protected readonly months = computed(() => {
    const year = this.filters().year;
    const counts = Array<number>(12).fill(0);
    for (const period of this.periods().years) {
      if (year !== undefined && period.year !== year) continue;
      for (const { month, count } of period.months) counts[month - 1] += count;
    }
    return counts.map((count, index) => ({ month: index + 1, count }));
  });
  protected readonly hasFilters = hasTimelineFilters;
  constructor() {
    effect(() => {
      const filters = this.filters();
      this.year.setValue(filters.year?.toString() ?? '', { emitEvent: false });
      this.month.setValue(filters.month?.toString() ?? '', { emitEvent: false });
    });
    this.year.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) =>
      this.changed.emit({
        ...this.filters(),
        year: value ? +value : undefined,
      }),
    );
    this.month.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((value) =>
        this.changed.emit({ ...this.filters(), month: value ? +value : undefined }),
      );
  }
  protected selectedYear() {
    return this.periods().years.find((period) => period.year === this.filters().year);
  }
  protected monthName(month: number): string {
    return new Intl.DateTimeFormat('es-AR', { month: 'long', timeZone: 'UTC' }).format(
      new Date(Date.UTC(2000, month - 1, 1)),
    );
  }
  protected typeState(group: TypeGroup): boolean | 'mixed' {
    const types = TYPE_GROUPS[group];
    const current = this.filters().type ?? [];
    if (types.every((type) => current.includes(type))) return true;
    return types.some((type) => current.includes(type)) ? 'mixed' : false;
  }
  protected toggleType(group: TypeGroup): void {
    const groupTypes: readonly TimelineType[] = TYPE_GROUPS[group];
    const current = this.filters().type ?? [];
    const select = this.typeState(group) !== true;
    const types = TIMELINE_TYPES.filter((value) =>
      groupTypes.includes(value) ? select : current.includes(value),
    );
    this.changed.emit({ ...this.filters(), type: types.length ? types : undefined });
  }
}
