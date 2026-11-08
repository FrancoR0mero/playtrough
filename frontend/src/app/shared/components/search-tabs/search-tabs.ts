import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-search-tabs',
  imports: [RouterLink],
  template: `
    <nav aria-label="Tipo de búsqueda" class="mt-6 flex gap-1 border-b border-white/10">
      @for (tab of tabs; track tab.path) {
        <a
          [routerLink]="tab.path"
          [queryParams]="{ q: query().trim() || null }"
          [attr.aria-current]="active() === tab.id ? 'page' : null"
          [class.border-accent]="active() === tab.id"
          [class.border-transparent]="active() !== tab.id"
          class="inline-flex min-h-11 items-center border-b-2 px-5 py-3 text-sm font-semibold text-white hover:bg-white/5"
          >{{ tab.label }}</a
        >
      }
    </nav>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchTabsComponent {
  readonly active = input.required<'games' | 'people'>();
  readonly query = input('');
  protected readonly tabs = [
    { id: 'games', path: '/games', label: 'Juegos' },
    { id: 'people', path: '/people', label: 'Personas' },
  ];
}
