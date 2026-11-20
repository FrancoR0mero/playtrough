import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { ReviewContentComponent } from '../reviews/review-content';
import { GroupReview, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-reviews',
  imports: [RouterLink, ReviewContentComponent],
  templateUrl: './group-reviews.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupReviewsComponent implements OnInit {
  readonly groupId = input.required<number>();
  readonly accessLost = output<void>();
  private readonly api = inject(GroupsApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly items = signal<GroupReview[]>([]);
  protected readonly cursor = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    if (this.loading()) return;
    const cursor = this.cursor();
    this.loading.set(true);
    this.error.set('');
    this.api
      .reviews(this.groupId(), cursor)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.items.update((items) => (cursor === null ? page.items : [...items, ...page.items]));
          this.cursor.set(page.nextCursor);
          this.loading.set(false);
        },
        error: (response: HttpErrorResponse) => {
          this.loading.set(false);
          if (response.status === 404) {
            this.items.set([]);
            this.cursor.set(null);
            this.error.set('Las reseñas ya no están disponibles.');
            this.accessLost.emit();
          } else {
            this.error.set('No pudimos cargar las reseñas.');
          }
        },
      });
  }
}
