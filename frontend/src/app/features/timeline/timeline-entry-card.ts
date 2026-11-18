import { Dialog, DialogRef } from '@angular/cdk/dialog';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
  computed,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { GameDetailComponent } from '../games/game-detail';
import { GameImageComponent } from '../games/game-image';
import { GameSummary } from '../games/models/game';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from '../profile/profile-error';
import { ReactionTarget } from '../reactions/models/reaction';
import { ReactionButtonsComponent } from '../reactions/reaction-buttons';
import { TimelineService } from './services/timeline';
import { TimelineEntryMenuComponent } from './timeline-entry-menu';
import { EntryLikesDialogComponent, EntryLikesDialogData } from './entry-likes-dialog';
import { EntryOrigin, entryLinkId } from './entry-navigation';
import {
  FEED_TEXT,
  effectiveFeedDate,
  feedTimestamp,
  fullFeedDate,
  relativeFeedTime,
} from '../feed/feed-text';
import { FeedEntryDto, TimelineEntryDto, TimelinePerspective } from './models/timeline';
import {
  TIMELINE_IMAGE_ASPECT_RATIO,
  TIMELINE_TEXT,
  entryAction,
  timelineDate,
} from './timeline-text';

@Component({
  selector: 'app-timeline-entry-card',
  imports: [
    RouterLink,
    GameImageComponent,
    StarRatingComponent,
    UserAvatarComponent,
    TimelineEntryMenuComponent,
    ReactionButtonsComponent,
  ],
  templateUrl: './timeline-entry-card.html',
  host: { class: 'block min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineEntryCardComponent {
  readonly entry = input.required<TimelineEntryDto>();
  readonly perspective = input<TimelinePerspective>('self');
  readonly showAuthor = input(false);
  readonly showActions = input(false);
  readonly detail = input(false);
  readonly originPages = input(1);
  readonly commentCount = input<number>();
  readonly compose = output<void>();
  readonly referenceTime = input(Date.now());
  protected readonly feedText = FEED_TEXT;
  protected readonly publishedAt = computed(() => feedTimestamp(this.entry().recordedAt));
  protected readonly relativeTime = computed(() =>
    relativeFeedTime(this.entry().recordedAt, this.referenceTime()),
  );
  protected readonly fullDate = fullFeedDate;
  protected readonly effectiveDate = effectiveFeedDate;
  readonly updated = output<TimelineEntryDto>();
  readonly missing = output<number>();
  protected readonly busy = signal(false);
  protected readonly visibilityError = signal(false);
  protected readonly status = signal('');
  protected readonly cooldown = createRateLimitCooldown();
  private readonly timeline = inject(TimelineService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly menu = viewChild(TimelineEntryMenuComponent);
  protected readonly text = TIMELINE_TEXT;
  protected readonly aspectRatio = TIMELINE_IMAGE_ASPECT_RATIO;
  protected readonly action = entryAction;
  protected readonly date = timelineDate;
  protected readonly reactionTarget = computed<ReactionTarget>(() => ({
    type: 'entry',
    id: this.entry().id,
  }));
  protected readonly commentsLabel = computed(() => {
    const count = this.commentCount() ?? this.entry().commentCount;
    return `${count} ${count === 1 ? 'comentario' : 'comentarios'}`;
  });
  protected readonly entryLinkId = entryLinkId;
  protected origin(link: EntryOrigin['link']): { entryOrigin: EntryOrigin } {
    return {
      entryOrigin: { url: this.router.url, id: this.entry().id, link, pages: this.originPages() },
    };
  }
  private readonly dialogs = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private preview?: DialogRef<void, GameDetailComponent>;
  private likesPanel?: DialogRef<void, EntryLikesDialogComponent>;

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.preview?.close();
      this.likesPanel?.close();
    });
  }
  protected author() {
    return (this.entry() as Partial<FeedEntryDto>).author;
  }
  protected changeVisibility(hidden: boolean): void {
    if (
      !this.showActions() ||
      this.perspective() !== 'self' ||
      this.busy() ||
      this.cooldown.remaining()
    )
      return;
    this.busy.set(true);
    this.visibilityError.set(false);
    this.status.set('');
    this.timeline
      .setVisibility(this.entry().id, hidden)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry) => {
          this.busy.set(false);
          this.status.set(entry.isHidden ? this.text.hiddenDone : this.text.shownDone);
          this.updated.emit(entry);
          afterNextRender(() => this.menu()?.focusTrigger(), { injector: this.injector });
        },
        error: (error: unknown) => {
          this.busy.set(false);
          if (profileError(error).code === 'entry_not_found') {
            this.missing.emit(this.entry().id);
            return;
          }
          this.cooldown.handle(error);
          this.visibilityError.set(true);
          afterNextRender(() => this.menu()?.focusTrigger(), { injector: this.injector });
        },
      });
  }
  protected openGame(origin: HTMLButtonElement): void {
    if (this.preview || this.likesPanel || this.busy()) return;
    const game: GameSummary = { ...this.entry().game, platforms: [] };
    this.preview = this.dialogs.open<void, GameSummary, GameDetailComponent>(GameDetailComponent, {
      data: game,
      role: 'dialog',
      ariaModal: true,
      ariaLabelledBy: 'game-detail-title',
      autoFocus: '[data-action="close"]',
      restoreFocus: origin,
      width: '100vw',
      maxWidth: '64rem',
    });
    this.preview.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.preview = undefined;
    });
  }

  protected openLikes(origin: HTMLButtonElement): void {
    if (this.dialogs.openDialogs.length || this.busy()) return;
    this.likesPanel = this.dialogs.open<void, EntryLikesDialogData, EntryLikesDialogComponent>(
      EntryLikesDialogComponent,
      {
        data: { entryId: this.entry().id },
        role: 'dialog',
        ariaModal: true,
        ariaLabelledBy: 'entry-likes-title',
        ariaDescribedBy: 'entry-likes-note',
        autoFocus: '[data-close-likes]',
        restoreFocus: origin,
        width: 'calc(100vw - 2rem)',
        maxWidth: '28rem',
      },
    );
    this.likesPanel.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.likesPanel = undefined;
    });
  }
}
