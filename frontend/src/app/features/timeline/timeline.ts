import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription, merge } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { LibraryService } from '../library/services/library';
import { profileError } from '../profile/profile-error';
import { ReviewService } from '../reviews/services/review';
import { TimelineEntryDto } from './models/timeline';
import { TimelineService } from './services/timeline';
import { TimelineEntryCardComponent } from './timeline-entry-card';
import { TIMELINE_IMAGE_ASPECT_RATIO, TIMELINE_TEXT, timelineDate } from './timeline-text';

@Component({
  selector: 'app-timeline',
  imports: [RouterLink, TimelineEntryCardComponent],
  templateUrl: './timeline.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineComponent {
  readonly source = input('me');
  readonly readonly = input(false);
  readonly active = input(true);
  protected readonly text = TIMELINE_TEXT;
  protected readonly aspectRatio = TIMELINE_IMAGE_ASPECT_RATIO;
  protected readonly items = signal<TimelineEntryDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly hasMore = signal(false);
  protected readonly announcement = signal('');
  protected readonly cooldown = createRateLimitCooldown();
  private readonly service = inject(TimelineService);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private cursor: string | null = null;
  private dirty = true;
  private revision = 0;
  private snapshotRevision = 0;
  private previousSource?: string;

  constructor() {
    merge(inject(LibraryService).changes$, inject(ReviewService).changes$)
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (this.source() === 'me') {
          this.dirty = true;
          this.revision++;
        }
      });
    effect(() => {
      const active = this.active();
      const source = this.source();
      untracked(() => {
        if (source !== this.previousSource) {
          this.request?.unsubscribe();
          this.loading.set(false);
          this.items.set([]);
          this.loaded.set(false);
          this.dirty = true;
          this.previousSource = source;
        }
        if (!active) {
          if (this.loading()) {
            this.request?.unsubscribe();
            this.loading.set(false);
            this.dirty = true;
          }
        } else if (this.dirty) this.load(true);
      });
    });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }
  protected month(entry: TimelineEntryDto): string {
    return timelineDate(`${entry.occurredOn.slice(0, 7)}-01`, true);
  }
  protected startsMonth(index: number): boolean {
    return (
      index === 0 ||
      this.items()[index].occurredOn.slice(0, 7) !== this.items()[index - 1].occurredOn.slice(0, 7)
    );
  }
  protected retry(): void {
    this.load(!this.loaded() || this.dirty);
  }
  protected more(): void {
    if (this.hasMore()) this.load(false);
  }
  private load(reset: boolean): void {
    if (this.loading() || this.cooldown.remaining()) return;
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.announcement.set('');
    if (reset) {
      this.snapshotRevision = this.revision;
      this.items.set([]);
      this.cursor = null;
      this.hasMore.set(false);
      this.loaded.set(false);
    }
    const query = { limit: 20, ...(this.cursor ? { cursor: this.cursor } : {}) };
    const request =
      this.source() === 'me'
        ? this.service.getMine(query)
        : this.service.getUser(this.source(), query);
    this.request = request.subscribe({
      next: (page) => {
        const entries = new Map(this.items().map((entry) => [entry.id, entry]));
        for (const entry of page.items) entries.set(entry.id, entry);
        this.items.set([...entries.values()]);
        this.cursor = page.nextCursor;
        this.hasMore.set(page.hasMore && page.nextCursor !== null);
        this.loaded.set(true);
        this.loading.set(false);
        this.dirty = this.snapshotRevision !== this.revision;
        this.announcement.set(this.text.loaded(this.items().length));
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.cooldown.handle(error);
        if (profileError(error).code === 'invalid_cursor') {
          this.dirty = true;
          this.error.set(this.text.invalidCursor);
        } else this.error.set(this.text.error);
      },
    });
  }
}
