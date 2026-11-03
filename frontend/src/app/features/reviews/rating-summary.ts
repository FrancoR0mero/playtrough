import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating';
import { RatingSummary } from './models/review';

@Component({
  selector: 'app-rating-summary',
  imports: [StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-star-rating [value]="summary().average" />
    @if (summary().count > 0) {
      <p class="mt-2 text-sm text-slate-300">
        {{ average() }} · {{ summary().count }} {{ summary().count === 1 ? 'reseña' : 'reseñas' }}
      </p>
    } @else {
      <p class="mt-2 text-sm text-slate-300">Sin reseñas todavía</p>
    }
  `,
})
export class RatingSummaryComponent {
  readonly summary = input.required<RatingSummary>();
  protected readonly average = computed(
    () =>
      this.summary().average?.toLocaleString('es-AR', {
        minimumFractionDigits: 1,
        maximumFractionDigits: 1,
      }) ?? '',
  );
}
