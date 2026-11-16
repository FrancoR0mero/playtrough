import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  Injector,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, Subscription, auditTime, merge } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { LibraryService } from '../library/services/library';
import { RelationshipDto } from '../friends/models/friend';
import { FriendshipButtonComponent } from '../friends/friendship-button';
import { FriendsService } from '../friends/services/friends';
import { ProfileRestriction } from '../profile/models/user';
import { ProfileRestrictionComponent } from '../profile/profile-restriction';
import { profileError } from '../profile/profile-error';
import { ReviewService } from '../reviews/services/review';
import { TimelineEntryDto, TimelinePeriods } from './models/timeline';
import { TimelineFilterPanelComponent } from './timeline-filter-panel';
import { TimelineFilters, hasTimelineFilters, parseTimelineFilters } from './timeline-filters';
import { TimelineService } from './services/timeline';
import { TimelineEntryCardComponent } from './timeline-entry-card';
import { TIMELINE_IMAGE_ASPECT_RATIO, TIMELINE_TEXT, timelineDate } from './timeline-text';

@Component({
  selector: 'app-timeline',
  imports: [
    RouterLink,
    TimelineEntryCardComponent,
    ProfileRestrictionComponent,
    FriendshipButtonComponent,
    TimelineFilterPanelComponent,
  ],
  templateUrl: './timeline.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineComponent {
  readonly source = input('me');
  readonly readonly = input(false);
  readonly active = input(true);
  readonly relationship = input<RelationshipDto | null>(null);
  readonly accessChanged = output<void>();
  protected readonly text = TIMELINE_TEXT;
  protected readonly aspectRatio = TIMELINE_IMAGE_ASPECT_RATIO;
  protected readonly items = signal<TimelineEntryDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly hasMore = signal(false);
  protected readonly announcement = signal('');
  protected readonly restricted = signal(false);
  protected readonly restriction = signal<ProfileRestriction | null>(null);
  protected readonly notFound = signal(false);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly filters = signal<TimelineFilters>({});
  protected readonly periods = signal<TimelinePeriods>({ years: [] });
  protected readonly periodsLoading = signal(false);
  protected readonly periodsError = signal(false);
  protected readonly filterWarning = signal(false);
  protected readonly entryNotice = signal(false);
  protected readonly hasFilters = hasTimelineFilters;
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly root = viewChild<ElementRef<HTMLElement>>('timelineRoot');
  private periodsRequest?: Subscription;
  private previousFilters?: string;
  private readonly service = inject(TimelineService);
  private readonly destroyRef = inject(DestroyRef);
  private request?: Subscription;
  private cursor: string | null = null;
  private dirty = true;
  private revision = 0;
  private snapshotRevision = 0;
  private previousSource?: string;
  private readonly accessRefreshes = new Subject<void>();

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const filters = parseTimelineFilters(params);
      this.filters.set(filters ?? {});
      if (filters === null) {
        this.filterWarning.set(true);
        this.writeFilters({}, true);
      }
    });
    this.accessRefreshes.pipe(auditTime(0), takeUntilDestroyed()).subscribe(() => {
      if (this.active()) this.load(true);
    });
    inject(FriendsService)
      .changes$.pipe(takeUntilDestroyed())
      .subscribe((change) => {
        if (this.source() !== 'me' && change.username.toLowerCase() === this.source().toLowerCase())
          this.refreshAccess();
      });
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
      const filters = JSON.stringify(this.filters());
      untracked(() => {
        if (source !== this.previousSource || filters !== this.previousFilters) {
          this.request?.unsubscribe();
          this.periodsRequest?.unsubscribe();
          this.periods.set({ years: [] });
          this.periodsLoading.set(false);
          this.periodsError.set(false);
          this.loading.set(false);
          this.items.set([]);
          this.loaded.set(false);
          this.hasMore.set(false);
          this.cursor = null;
          this.announcement.set('');
          this.restricted.set(false);
          this.notFound.set(false);
          this.error.set(null);
          this.dirty = true;
          this.previousSource = source;
          this.previousFilters = filters;
        }
        if (!active) {
          if (this.loading() || this.periodsLoading()) {
            this.request?.unsubscribe();
            this.periodsRequest?.unsubscribe();
            this.periodsLoading.set(false);
            this.loading.set(false);
            this.dirty = true;
          }
        } else if (this.dirty) this.load(true);
      });
    });
    this.destroyRef.onDestroy(() => {
      this.request?.unsubscribe();
      this.periodsRequest?.unsubscribe();
    });
  }
  protected changeFilters(filters: TimelineFilters): void {
    this.filterWarning.set(false);
    this.entryNotice.set(false);
    this.writeFilters(filters);
  }
  private writeFilters(filters: TimelineFilters, replaceUrl = false): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParamsHandling: 'merge',
      replaceUrl,
      queryParams: {
        year: filters.year ?? null,
        month: filters.month ?? null,
        type: filters.type?.length ? filters.type.join(',') : null,
      },
    });
  }
  protected entryUpdated(entry: TimelineEntryDto): void {
    this.items.update((items) => items.map((item) => (item.id === entry.id ? entry : item)));
    this.loadPeriods();
  }
  protected entryMissing(id: number): void {
    this.items.update((items) => items.filter((item) => item.id !== id));
    this.entryNotice.set(true);
    this.loadPeriods();
    afterNextRender(() => this.root()?.nativeElement.focus(), { injector: this.injector });
  }
  protected loadPeriods(): void {
    this.periodsRequest?.unsubscribe();
    if (this.cooldown.remaining()) {
      this.periodsError.set(true);
      return;
    }
    this.periodsError.set(false);
    this.periodsLoading.set(true);
    const types = this.filters().type;
    const request =
      this.source() === 'me'
        ? this.service.getMyPeriods(types)
        : this.service.getUserPeriods(this.source(), types);
    this.periodsRequest = request.subscribe({
      next: (periods) => {
        this.periods.set(periods);
        this.periodsLoading.set(false);
      },
      error: (error: unknown) => {
        this.periodsLoading.set(false);
        this.cooldown.handle(error);
        if (this.handleAccessError(error)) return;
        if (profileError(error).code === 'invalid_filter') this.invalidFilters();
        else this.periodsError.set(true);
      },
    });
  }
  private invalidFilters(): void {
    this.filterWarning.set(true);
    if (hasTimelineFilters(this.filters())) this.writeFilters({}, true);
    else this.error.set(this.text.invalidFilters);
  }
  private handleAccessError(error: unknown): boolean {
    const code = profileError(error).code;
    if (
      this.source() === 'me' ||
      !(error instanceof HttpErrorResponse) ||
      !(
        (error.status === 403 && code === 'history_restricted') ||
        (error.status === 404 && code === 'user_not_found')
      )
    )
      return false;
    this.request?.unsubscribe();
    this.periodsRequest?.unsubscribe();
    this.loading.set(false);
    this.periodsLoading.set(false);
    this.periodsError.set(false);
    this.items.set([]);
    this.periods.set({ years: [] });
    this.hasMore.set(false);
    this.cursor = null;
    this.loaded.set(false);
    this.dirty = false;
    if (error.status === 404) this.notFound.set(true);
    else {
      const restriction = error.error?.extensions?.restriction ?? error.error?.restriction;
      this.restriction.set(
        restriction === 'private' || restriction === 'friends_only' ? restriction : null,
      );
      this.restricted.set(true);
    }
    return true;
  }
  protected relationshipChanged(): void {
    this.refreshAccess();
    this.accessChanged.emit();
  }
  private refreshAccess(): void {
    this.request?.unsubscribe();
    this.periodsRequest?.unsubscribe();
    this.periods.set({ years: [] });
    this.periodsLoading.set(false);
    this.loading.set(false);
    this.items.set([]);
    this.hasMore.set(false);
    this.loaded.set(false);
    this.announcement.set('');
    this.restricted.set(false);
    this.notFound.set(false);
    this.error.set(null);
    this.dirty = true;
    this.accessRefreshes.next();
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
    if (this.loading()) return;
    if (this.cooldown.remaining()) {
      this.error.set(this.text.error);
      return;
    }
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.announcement.set('');
    if (reset) {
      this.restricted.set(false);
      this.notFound.set(false);
      this.snapshotRevision = this.revision;
      this.items.set([]);
      this.cursor = null;
      this.hasMore.set(false);
      this.loaded.set(false);
    }
    const query = { limit: 20, ...(this.cursor ? { cursor: this.cursor } : {}) };
    const request =
      this.source() === 'me'
        ? this.service.getMine({ ...query, ...this.filters() })
        : this.service.getUser(this.source(), { ...query, ...this.filters() });
    this.request = request.subscribe({
      next: (page) => {
        const entries = new Map(this.items().map((entry) => [entry.id, entry]));
        for (const entry of page.items)
          if (this.source() === 'me' || !entry.isHidden) entries.set(entry.id, entry);
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
        if (this.handleAccessError(error)) return;
        if (profileError(error).code === 'invalid_filter') {
          this.invalidFilters();
          return;
        }
        if (profileError(error).code === 'invalid_cursor') {
          this.dirty = true;
          this.error.set(this.text.invalidCursor);
        } else this.error.set(this.text.error);
      },
    });
    if (reset && !this.restricted() && !this.notFound() && !this.cooldown.remaining())
      this.loadPeriods();
  }
}
