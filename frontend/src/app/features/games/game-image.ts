import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

@Component({
  selector: 'app-game-image',
  host: { class: 'block h-full w-full' },
  imports: [NgTemplateOutlet],
  template: `
    @if (imageUrl(); as url) {
      <img
        [src]="url"
        alt=""
        loading="lazy"
        class="h-full w-full object-cover object-center"
        (error)="markFailed(url)"
      />
    } @else {
      <ng-container [ngTemplateOutlet]="placeholder" />
    }
    <ng-template #placeholder>
      <div
        data-placeholder
        class="flex h-full w-full flex-col items-center justify-center gap-3 bg-slate-800 px-4 pb-12 text-slate-400"
      >
        <svg
          aria-hidden="true"
          class="h-10 w-10"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="1.2"
        >
          <rect x="3" y="5" width="18" height="14" rx="4" />
          <path d="M6 12h6M9 9v6M16 10h.01M18 14h.01" stroke-linecap="round" />
        </svg>
        <span class="text-center text-xs">Imagen no disponible</span>
      </div>
    </ng-template>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameImageComponent {
  readonly src = input<string | null>(null);
  readonly fallbackSrc = input<string | null>(null);
  private readonly failedUrls = signal<ReadonlySet<string>>(new Set());
  protected readonly imageUrl = computed(
    () =>
      [this.src(), this.fallbackSrc()].find((url) => url && !this.failedUrls().has(url)) ?? null,
  );

  protected markFailed(url: string): void {
    this.failedUrls.update((urls) => new Set([...urls, url]));
  }
}
