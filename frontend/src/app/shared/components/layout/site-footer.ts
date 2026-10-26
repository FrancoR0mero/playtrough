import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RawgAttributionComponent } from './rawg-attribution';

@Component({
  selector: 'app-site-footer',
  imports: [RouterLink, RawgAttributionComponent],
  template: `
    <footer class="site-footer">
      <a routerLink="/" class="font-semibold text-slate-300">
        Playtrough<span class="text-indigo-400">.</span>
      </a>
      <p>Cada juego, una historia. Cada partida, tu recorrido.</p>
      <app-rawg-attribution />
    </footer>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteFooterComponent {}
