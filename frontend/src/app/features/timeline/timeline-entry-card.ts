import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { StarRatingComponent } from '../../shared/components/star-rating/star-rating';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { GameDetailComponent } from '../games/game-detail';
import { GameImageComponent } from '../games/game-image';
import { GameSummary } from '../games/models/game';
import { FeedEntryDto, TimelineEntryDto, TimelinePerspective } from './models/timeline';
import {
  TIMELINE_IMAGE_ASPECT_RATIO,
  TIMELINE_TEXT,
  entryAction,
  timelineDate,
} from './timeline-text';

@Component({
  selector: 'app-timeline-entry-card',
  imports: [RouterLink, GameImageComponent, StarRatingComponent, UserAvatarComponent],
  templateUrl: './timeline-entry-card.html',
  host: { class: 'block min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TimelineEntryCardComponent {
  readonly entry = input.required<TimelineEntryDto>();
  readonly perspective = input<TimelinePerspective>('self');
  readonly showAuthor = input(false);
  readonly showActions = input(false);
  protected readonly text = TIMELINE_TEXT;
  protected readonly aspectRatio = TIMELINE_IMAGE_ASPECT_RATIO;
  protected readonly action = entryAction;
  protected readonly date = timelineDate;
  private readonly dialogs = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private preview?: DialogRef<void, GameDetailComponent>;

  constructor() {
    this.destroyRef.onDestroy(() => this.preview?.close());
  }
  protected author() {
    return (this.entry() as Partial<FeedEntryDto>).author;
  }
  protected openGame(origin: HTMLButtonElement): void {
    if (this.preview) return;
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
}
