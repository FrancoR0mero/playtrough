import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-gamebrain-attribution',
  template: `
    <p class="text-xs leading-relaxed text-slate-400">
      Datos e imágenes de videojuegos provistos por
      <a
        href="https://gamebrain.co/api"
        target="_blank"
        rel="noopener noreferrer"
        class="rounded text-slate-300 underline underline-offset-4 hover:text-white"
        >Video Games API</a
      >
    </p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameBrainAttributionComponent {}
