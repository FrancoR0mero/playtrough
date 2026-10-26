import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RawgAttributionComponent } from '../../shared/components/layout/rawg-attribution';
import { GameImageComponent } from './game-image';
import { GameSummary } from './models/game';

// Phase 1 preview: the detail request and remaining sections belong to US-21.
@Component({
  selector: 'app-game-preview',
  imports: [GameImageComponent, RawgAttributionComponent],
  template: `
    <article
      class="relative flex min-h-dvh flex-col bg-background text-slate-200 sm:min-h-0 sm:overflow-hidden sm:rounded-2xl sm:border sm:border-slate-700"
    >
      <button
        type="button"
        data-action="close"
        (click)="dialog.close()"
        aria-label="Cerrar detalle del juego"
        class="ui-button absolute top-3 right-3 z-10 border border-white/30 bg-black/80 px-4 py-2 text-white"
      >
        Cerrar
      </button>
      <div class="relative aspect-video overflow-hidden">
        <app-game-image class="absolute inset-0" [src]="game.coverUrl" />
        <div class="absolute inset-0 bg-gradient-to-t from-black via-black/20 to-transparent"></div>
        <div class="absolute right-5 bottom-5 left-5 sm:right-8 sm:left-8">
          <h2 id="game-preview-title" class="text-2xl font-semibold text-white sm:text-4xl">
            {{ game.name }}
          </h2>
          @if (game.releaseYear) {
            <p class="mt-2 text-sm text-slate-200">{{ game.releaseYear }}</p>
          }
        </div>
      </div>
      <div class="mt-auto p-5 sm:p-8"><app-rawg-attribution /></div>
    </article>
  `,
  styles: `
    :host {
      display: block;
      max-height: 100dvh;
      overflow-y: auto;
    }
    @media (min-width: 640px) {
      :host {
        max-height: 90dvh;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GamePreviewComponent {
  protected readonly game = inject<GameSummary>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<void>>(DialogRef);
}
