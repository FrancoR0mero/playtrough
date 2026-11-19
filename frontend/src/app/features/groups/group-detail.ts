import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { GroupsApi, SocialGroup } from './groups-api';

@Component({
  selector: 'app-group-detail',
  imports: [RouterLink],
  template: `
    <section class="page-container">
      <a
        routerLink="/groups"
        class="inline-flex min-h-11 items-center text-sm text-indigo-300 underline"
        >Volver a grupos</a
      >
      @if (group(); as item) {
        <h1 class="page-heading mt-3 wrap-anywhere text-white">{{ item.name }}</h1>
        <div class="mt-4 flex flex-wrap gap-x-4 gap-y-1 border-y border-white/10 py-3 text-sm">
          <span class="font-medium text-indigo-300">{{ item.topic }}</span>
          <span class="text-slate-300">{{
            item.privacy === 'private' ? 'Privado' : 'Público'
          }}</span>
        </div>
        <p
          class="mt-5 max-w-prose wrap-anywhere whitespace-pre-line leading-relaxed text-slate-200"
        >
          {{ item.description }}
        </p>
      } @else if (error()) {
        <p role="alert" class="mt-3 text-sm text-red-300">{{ error() }}</p>
      } @else {
        <p role="status" class="mt-3 text-sm text-slate-300">Cargando grupo…</p>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupDetailComponent {
  protected readonly group = signal<SocialGroup | null>(null);
  protected readonly error = signal('');

  constructor() {
    const api = inject(GroupsApi);
    inject(ActivatedRoute)
      .paramMap.pipe(
        switchMap((params) => {
          this.group.set(null);
          this.error.set('');
          return api.get(params.get('id')!).pipe(
            catchError((response: HttpErrorResponse) => {
              this.error.set(
                response.status === 404
                  ? 'El grupo no está disponible.'
                  : 'No pudimos cargar el grupo.',
              );
              return of(null);
            }),
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((group) => this.group.set(group));
  }
}
