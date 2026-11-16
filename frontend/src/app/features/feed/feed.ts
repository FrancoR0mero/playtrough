import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription, fromEvent, merge } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { FriendsService } from '../friends/services/friends';
import { FriendsState } from '../friends/services/friends-state';
import { profileError } from '../profile/profile-error';
import { FeedEntryDto, TimelinePage } from '../timeline/models/timeline';
import { TimelineService } from '../timeline/services/timeline';
import { TimelineEntryCardComponent } from '../timeline/timeline-entry-card';
import { TIMELINE_IMAGE_ASPECT_RATIO } from '../timeline/timeline-text';
import { FEED_REFRESH_INTERVAL_MS, FEED_TEXT } from './feed-text';

@Component({
  selector: 'app-feed',
  imports: [RouterLink, TimelineEntryCardComponent],
  templateUrl: './feed.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeedComponent implements OnInit {
  protected readonly text = FEED_TEXT;
  protected readonly aspectRatio = TIMELINE_IMAGE_ASPECT_RATIO;
  protected readonly friends = inject(FriendsState);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly items = signal<FeedEntryDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly checking = signal(false);
  protected readonly loaded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly pending = signal<TimelinePage<FeedEntryDto> | null>(null);
  protected readonly announcement = signal('');
  protected readonly displayTime = signal(Date.now());
  private readonly clock = signal(Date.now());
  private readonly lastCheck = signal<number | null>(null);
  protected readonly refreshWait = computed(() =>
    Math.max(
      this.cooldown.remaining(),
      Math.ceil(
        Math.max(0, (this.lastCheck() ?? -Infinity) + FEED_REFRESH_INTERVAL_MS - this.clock()) /
          1000,
      ),
    ),
  );
  private readonly service = inject(TimelineService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly title = viewChild<ElementRef<HTMLHeadingElement>>('title');
  private request?: Subscription;
  private cursor: string | null = null;
  protected failed: 'first' | 'page' | 'refresh' = 'first';
  private dirty = false;
  constructor() {
    const timer = setInterval(() => {
      const now = Date.now();
      this.clock.set(now);
      if (Math.floor(now / 60_000) !== Math.floor(this.displayTime() / 60_000))
        this.displayTime.set(now);
    }, 1000);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      this.request?.unsubscribe();
    });
    merge(fromEvent(document, 'visibilitychange'), fromEvent(window, 'focus'))
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (document.visibilityState !== 'visible') return;
        if (this.dirty) this.load(true);
        else this.check(false);
      });
    inject(FriendsService)
      .changes$.pipe(takeUntilDestroyed())
      .subscribe(() => {
        this.request?.unsubscribe();
        this.loading.set(false);
        this.checking.set(false);
        this.pending.set(null);
        this.items.set([]);
        this.hasMore.set(false);
        this.loaded.set(false);
        this.error.set(null);
        this.announcement.set('');
        this.cursor = null;
        this.dirty = true;
        if (document.visibilityState === 'visible') this.load(true);
      });
    afterNextRender(() => this.title()?.nativeElement.focus());
  }
  ngOnInit(): void {
    this.load(true);
  }
  protected more(): void {
    if (this.hasMore()) this.load(false);
  }
  protected retry(): void {
    if (this.failed === 'refresh') this.check(true);
    else this.load(this.failed === 'first');
  }
  protected check(manual: boolean): void {
    this.clock.set(Date.now());
    if (!this.loaded() || this.loading() || this.checking() || this.refreshWait()) return;
    this.checking.set(true);
    this.lastCheck.set(Date.now());
    if (manual) this.error.set(null);
    this.request = this.service
      .getFeed({ limit: 20 })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.checking.set(false);
          if (this.failed === 'refresh') this.error.set(null);
          const current = this.items()[0]?.id,
            fresh = page.items.find((entry) => !entry.isHidden)?.id;
          this.pending.set(current !== fresh ? page : null);
        },
        error: (error: unknown) => {
          this.checking.set(false);
          this.cooldown.handle(error);
          if (manual) {
            this.failed = 'refresh';
            this.error.set(this.text.refreshError);
          }
        },
      });
  }
  protected seeNews(): void {
    const page = this.pending();
    if (!page) return;
    this.request?.unsubscribe();
    this.loading.set(false);
    this.checking.set(false);
    this.apply(page, false);
    this.pending.set(null);
    this.error.set(null);
    this.title()?.nativeElement.focus();
  }
  private load(reset: boolean): void {
    if (this.loading() || this.checking()) return;
    if (this.cooldown.remaining()) {
      this.failed = 'first';
      this.error.set(this.text.error);
      return;
    }
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.announcement.set('');
    if (reset) {
      this.items.set([]);
      this.cursor = null;
      this.hasMore.set(false);
      this.loaded.set(false);
      this.pending.set(null);
      this.lastCheck.set(Date.now());
      this.clock.set(Date.now());
    }
    const query = { limit: 20, ...(this.cursor ? { cursor: this.cursor } : {}) };
    this.request = this.service
      .getFeed(query)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.loading.set(false);
          this.dirty = false;
          this.apply(page, !reset);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.cooldown.handle(error);
          const invalidCursor = profileError(error).code === 'invalid_cursor';
          this.failed = reset || invalidCursor ? 'first' : 'page';
          this.error.set(invalidCursor ? this.text.invalidCursor : this.text.error);
        },
      });
  }
  private apply(page: TimelinePage<FeedEntryDto>, append: boolean): void {
    const entries = new Map((append ? this.items() : []).map((entry) => [entry.id, entry]));
    for (const entry of page.items) if (!entry.isHidden) entries.set(entry.id, entry);
    this.items.set([...entries.values()]);
    this.cursor = page.nextCursor;
    this.hasMore.set(page.hasMore && page.nextCursor !== null);
    this.loaded.set(true);
    this.announcement.set(this.text.loaded(this.items().length));
  }
}
