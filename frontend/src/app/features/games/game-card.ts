import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { GameImageComponent } from './game-image';
import { GameSummary } from './models/game';

// RAWG supplies landscape images; keep cards and skeletons on the same crop.
export const GAME_CARD_ASPECT_RATIO = '3 / 4';

@Component({
  selector: 'app-game-card',
  imports: [GameImageComponent],
  templateUrl: './game-card.html',
  host: { class: 'block min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameCardComponent {
  readonly game = input.required<Pick<GameSummary, 'name' | 'coverUrl' | 'releaseYear'>>();
  readonly opened = output<HTMLButtonElement>();
  protected readonly aspectRatio = GAME_CARD_ASPECT_RATIO;
}
