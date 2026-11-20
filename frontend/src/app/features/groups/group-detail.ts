import { Dialog, DialogModule } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, Subject, switchMap } from 'rxjs';
import { GroupConfirmation, GroupConfirmationComponent } from './group-confirmation';
import { GroupPeopleComponent } from './group-people';
import { GroupReviewsComponent } from './group-reviews';
import { GroupProfile, GroupsApi } from './groups-api';

@Component({
  selector: 'app-group-detail',
  imports: [DialogModule, RouterLink, GroupPeopleComponent, GroupReviewsComponent],
  templateUrl: './group-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupDetailComponent {
  private readonly api = inject(GroupsApi);
  private readonly dialog = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly reload = new Subject<void>();

  protected readonly group = signal<GroupProfile | null>(null);
  protected readonly error = signal('');
  protected readonly actionError = signal('');
  protected readonly status = signal('');
  protected readonly pending = signal(false);
  protected readonly showReviews = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('tab') === 'reviews')),
    { initialValue: false },
  );

  constructor() {
    this.route.paramMap
      .pipe(
        switchMap((params) => {
          this.pending.set(false);
          this.status.set('');
          this.actionError.set('');
          const id = params.get('id')!;
          return this.reload.pipe(
            startWith(undefined),
            switchMap(() => {
              this.group.set(null);
              this.error.set('');
              return this.api.get(id).pipe(
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
          );
        }),
        takeUntilDestroyed(),
      )
      .subscribe((group) => this.group.set(group));
  }

  protected refresh(): void {
    if (!this.pending()) this.reload.next();
  }

  protected join(group: GroupProfile): void {
    if (this.pending()) return;
    this.pending.set(true);
    this.actionError.set('');
    this.api
      .join(group.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ status }) => {
          if (this.group()?.id !== group.id) return;
          this.pending.set(false);
          this.group.set({ ...group, membership: status });
          this.status.set(status === 'pending' ? 'Solicitud enviada.' : 'Te uniste al grupo.');
        },
        error: (response: HttpErrorResponse) => this.failed(group.id, response),
      });
  }

  protected confirmLeave(group: GroupProfile): void {
    if (this.pending()) return;
    const cancelling = group.membership === 'pending';
    const data: GroupConfirmation = cancelling
      ? {
          title: 'Cancelar solicitud',
          message: 'Podés volver a solicitar acceso más adelante.',
          label: 'Cancelar solicitud',
        }
      : {
          title: 'Salir del grupo',
          message: 'Vas a perder acceso a los miembros y a la actividad del grupo.',
          label: 'Salir del grupo',
        };
    this.dialog
      .open<boolean, GroupConfirmation>(GroupConfirmationComponent, {
        data,
        ariaLabelledBy: 'group-confirmation-title',
        ariaDescribedBy: 'group-confirmation-description',
      })
      .closed.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (confirmed && this.group()?.id === group.id) this.leave(group.id, cancelling);
      });
  }

  private leave(id: number, cancelling: boolean): void {
    this.pending.set(true);
    this.actionError.set('');
    this.api
      .leave(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          if (this.group()?.id !== id) return;
          this.pending.set(false);
          this.status.set(cancelling ? 'Solicitud cancelada.' : 'Saliste del grupo.');
          this.refresh();
        },
        error: (response: HttpErrorResponse) => this.failed(id, response),
      });
  }

  private failed(id: number, response: HttpErrorResponse): void {
    if (this.group()?.id !== id) return;
    this.pending.set(false);
    this.actionError.set(
      response.error?.detail || 'No pudimos actualizar tu membresía. Volvé a intentarlo.',
    );
  }
}
