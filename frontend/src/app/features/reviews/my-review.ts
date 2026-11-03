import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  Input,
  OnChanges,
  afterNextRender,
  computed,
  inject,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { MyReviewDto } from './models/review';
import { ReviewService } from './services/review';
import { reviewErrorCode, reviewErrorMessage } from './review-error';
import { normalizeReviewText, reviewTextLength } from './review-text';

@Component({
  selector: 'app-my-review',
  imports: [ReactiveFormsModule, StarRatingComponent],
  templateUrl: './my-review.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyReviewComponent implements OnChanges {
  @Input({ required: true }) gameId!: number;
  readonly editingChange = output<boolean>();
  readonly summaryInvalidated = output<void>();
  private readonly reviews = inject(ReviewService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private read?: Subscription;
  private mutation?: Subscription;
  private generation = 0;
  private origin = 'write-review';
  protected readonly mine = signal<MyReviewDto | null>(null);
  protected readonly loaded = signal(false);
  protected readonly loading = signal(false);
  readonly saving = signal(false);
  protected readonly editing = signal(false);
  protected readonly discard = signal(false);
  protected readonly removing = signal(false);
  protected readonly expanded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal('');
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly blocked = computed(
    () => this.saving() || this.loading() || this.cooldown.remaining() > 0,
  );
  protected readonly form = new FormGroup({
    rating: new FormControl<number | null>(null, [
      Validators.required,
      Validators.min(1),
      Validators.max(5),
    ]),
    text: new FormControl('', {
      nonNullable: true,
      validators: [
        (control) => (reviewTextLength(control.value) > 2000 ? { maxlength: true } : null),
      ],
    }),
  });
  private readonly values = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  protected readonly original = signal({ rating: null as number | null, text: '' });
  protected readonly count = computed(() => reviewTextLength(this.values().text ?? ''));
  protected readonly changed = computed(
    () =>
      this.values().rating !== this.original().rating ||
      (this.values().text ?? '') !== this.original().text,
  );
  protected readonly previewText = computed(() => {
    const text = this.mine()?.text ?? '';
    return this.expanded()
      ? text
      : Array.from(text).slice(0, 400).join('') + (Array.from(text).length > 400 ? '…' : '');
  });
  protected readonly longText = computed(() => Array.from(this.mine()?.text ?? '').length > 400);
  protected readonly formatNumber = (value: number) => value.toLocaleString('es-AR');
  protected readonly date = (value: string) =>
    new Intl.DateTimeFormat('es-AR', { day: '2-digit', month: '2-digit', year: 'numeric' }).format(
      new Date(value),
    );

  constructor() {
    this.reviews.changes$.pipe(takeUntilDestroyed()).subscribe((change) => {
      if (change.gameId !== this.gameId) return;
      this.read?.unsubscribe();
      this.mine.set(change.type === 'deleted' ? null : change.review);
      this.loaded.set(true);
    });
  }

  ngOnChanges(): void {
    this.generation++;
    this.read?.unsubscribe();
    this.mutation?.unsubscribe();
    this.form.enable();
    this.mine.set(null);
    this.loaded.set(false);
    this.error.set(null);
    this.notice.set('');
    this.removing.set(false);
    this.discard.set(false);
    this.expanded.set(false);
    this.editing.set(false);
    this.editingChange.emit(false);
    this.load();
  }

  protected load(): void {
    if (this.blocked()) return;
    this.loading.set(true);
    this.error.set(null);
    const generation = this.generation;
    this.read = this.reviews
      .getMine(this.gameId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (review) => {
          if (generation === this.generation) {
            this.mine.set(review);
            this.loaded.set(true);
          }
        },
        error: (error: unknown) => {
          if (generation !== this.generation) return;
          if (reviewErrorCode(error) === 'review_not_found') {
            this.mine.set(null);
            this.loaded.set(true);
          } else this.showError(error);
        },
      });
  }

  protected open(): void {
    if (this.blocked()) return;
    this.origin = this.mine() ? 'edit-review' : 'write-review';
    const value = { rating: this.mine()?.rating ?? null, text: this.mine()?.text ?? '' };
    this.original.set(value);
    this.form.reset(value);
    this.error.set(null);
    this.notice.set('');
    this.removing.set(false);
    this.editing.set(true);
    this.editingChange.emit(true);
  }

  back(): void {
    if (this.saving()) return;
    if (this.changed()) {
      this.discard.set(true);
      this.focus('keep-review');
    } else this.finish();
  }

  protected keep(): void {
    this.discard.set(false);
    this.focus('review-text');
  }

  protected finish(): void {
    this.discard.set(false);
    this.editing.set(false);
    this.editingChange.emit(false);
    this.error.set(null);
    this.focus(
      this.mine() ? 'edit-review' : this.origin === 'edit-review' ? 'write-review' : this.origin,
    );
  }

  protected save(): void {
    if (this.blocked() || this.form.invalid || !this.changed()) return;
    const { rating, text } = this.form.getRawValue();
    const wasEditing = this.original().rating !== null;
    const generation = this.generation;
    this.saving.set(true);
    this.error.set(null);
    this.form.disable();
    this.mutation = this.reviews
      .save(this.gameId, rating!, normalizeReviewText(text) || null)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.saving.set(false);
          this.form.enable();
        }),
      )
      .subscribe({
        next: ({ review }) => {
          if (generation !== this.generation) return;
          this.mine.set(review);
          this.loaded.set(true);
          this.finish();
          this.notice.set(wasEditing ? 'Reseña actualizada' : 'Reseña publicada');
        },
        error: (error: unknown) => {
          if (generation === this.generation) this.showError(error);
        },
      });
  }

  protected askRemove(): void {
    this.error.set(null);
    this.removing.set(true);
    this.focus('cancel-remove-review');
  }
  protected cancelRemove(): void {
    this.removing.set(false);
    this.focus('remove-review');
  }

  protected remove(): void {
    if (this.blocked() || !this.removing()) return;
    const generation = this.generation;
    this.saving.set(true);
    this.error.set(null);
    this.mutation = this.reviews
      .deleteMine(this.gameId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          if (generation === this.generation) this.removed();
        },
        error: (error: unknown) => {
          if (generation !== this.generation) return;
          if (reviewErrorCode(error) === 'review_not_found') {
            this.removed();
            this.summaryInvalidated.emit();
          } else this.showError(error);
        },
      });
  }

  private removed(): void {
    this.mine.set(null);
    this.removing.set(false);
    this.notice.set('Reseña eliminada');
    this.focus('write-review');
  }

  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 401) return;
    if (this.cooldown.handle(error)) this.error.set('Ya podés reintentar la operación.');
    else this.error.set(reviewErrorMessage(error));
  }

  private focus(action: string): void {
    afterNextRender(
      () =>
        this.element.nativeElement.querySelector<HTMLElement>(`[data-action="${action}"]`)?.focus(),
      { injector: this.injector },
    );
  }
}
