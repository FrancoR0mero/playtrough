import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating';
import { MyReviewDto } from './models/review';

@Component({
  selector: 'app-review-content',
  imports: [StarRatingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-star-rating [value]="review().rating" [size]="20" />
    <p class="mt-2 text-xs text-slate-400">
      <time [attr.datetime]="review().editedAt ?? review().createdAt">{{ date() }}</time>
      @if (review().editedAt) {
        <span>{{ ' (editada)' }}</span>
      }
    </p>
    @if (review().text; as text) {
      <!-- prettier-ignore -->
      <p [id]="textId()" data-review-text class="mt-4 whitespace-pre-line break-words text-sm leading-relaxed text-slate-300" [class.line-clamp-5]="long() && !expanded()">{{ text }}</p>
      @if (long()) {
        <button
          type="button"
          class="ui-button mt-2 min-h-11 text-sm text-violet-300 underline underline-offset-4"
          [attr.aria-controls]="textId()"
          [attr.aria-expanded]="expanded()"
          (click)="toggle()"
        >
          {{ expanded() ? 'Ver menos' : 'Ver más' }}
        </button>
      }
    }
  `,
})
export class ReviewContentComponent {
  readonly review = input.required<Omit<MyReviewDto, 'gameId'>>();
  readonly textId = input.required<string>();
  private readonly expandedId = signal<number | null>(null);
  protected readonly expanded = computed(() => this.expandedId() === this.review().id);
  protected readonly long = computed(
    () =>
      (this.review().text?.length ?? 0) > 400 || (this.review().text?.split('\n').length ?? 0) > 5,
  );
  protected readonly date = computed(() =>
    new Intl.DateTimeFormat('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(
      new Date(this.review().editedAt ?? this.review().createdAt),
    ),
  );
  protected toggle(): void {
    this.expandedId.set(this.expanded() ? null : this.review().id);
  }
}
