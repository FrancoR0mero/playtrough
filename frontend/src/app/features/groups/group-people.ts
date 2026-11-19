import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  OnInit,
  output,
  signal,
  WritableSignal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { GroupConfirmation, GroupConfirmationComponent } from './group-confirmation';
import { GroupDecision, GroupJoinRequest, GroupMember, GroupPage, GroupsApi } from './groups-api';

interface PeopleState<T> extends GroupPage<T> {
  loading: boolean;
  error: boolean;
}

function initialPage<T>(): PeopleState<T> {
  return { items: [], nextCursor: null, loading: false, error: false };
}

@Component({
  selector: 'app-group-people',
  imports: [DatePipe, RouterLink, UserAvatarComponent],
  templateUrl: './group-people.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupPeopleComponent implements OnInit {
  readonly groupId = input.required<number>();
  readonly owner = input.required<boolean>();
  readonly accessLost = output<void>();
  private readonly api = inject(GroupsApi);
  private readonly dialog = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly members = signal(initialPage<GroupMember>());
  protected readonly requests = signal(initialPage<GroupJoinRequest>());
  protected readonly saving = signal(false);
  protected readonly actionError = signal('');
  protected readonly status = signal('');

  ngOnInit(): void {
    this.loadMembers(null);
    if (this.owner()) this.loadRequests(null);
  }

  protected loadMembers(cursor: number | null): void {
    this.load(this.members, this.api.members(this.groupId(), cursor), cursor, true);
  }

  protected loadRequests(cursor: number | null): void {
    this.load(this.requests, this.api.joinRequests(this.groupId(), cursor), cursor, false);
  }

  protected decide(request: GroupJoinRequest, decision: GroupDecision): void {
    const label = decision === 'approve' ? 'Aprobar' : 'Rechazar';
    this.confirm(
      {
        title: label + ' solicitud',
        message: `¿Querés ${label.toLowerCase()} la solicitud de ${request.username}?`,
        label,
      },
      () => this.api.decide(this.groupId(), request.id, decision),
      decision === 'approve' ? 'Solicitud aprobada.' : 'Solicitud rechazada.',
    );
  }

  protected remove(member: GroupMember): void {
    this.confirm(
      {
        title: 'Expulsar miembro',
        message: `${member.username} perderá el acceso al interior del grupo.`,
        label: 'Expulsar',
      },
      () => this.api.remove(this.groupId(), member.userId),
      'Miembro expulsado.',
    );
  }

  private load<T>(
    state: WritableSignal<PeopleState<T>>,
    request: Observable<GroupPage<T>>,
    cursor: number | null,
    refreshOnDenied: boolean,
  ): void {
    if (state().loading) return;
    state.update((page) => ({ ...page, loading: true, error: false }));
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (page) =>
        state.update((previous) => ({
          items: cursor === null ? page.items : [...previous.items, ...page.items],
          nextCursor: page.nextCursor,
          loading: false,
          error: false,
        })),
      error: (response: HttpErrorResponse) => {
        if (response.status === 404) {
          state.set({ ...initialPage<T>(), error: true });
          if (refreshOnDenied) this.accessLost.emit();
        } else {
          state.update((page) => ({ ...page, loading: false, error: true }));
        }
      },
    });
  }

  private confirm(data: GroupConfirmation, request: () => Observable<void>, message: string): void {
    if (this.saving() || this.members().loading || this.requests().loading) return;
    this.dialog
      .open<boolean, GroupConfirmation>(GroupConfirmationComponent, {
        data,
        ariaLabelledBy: 'group-confirmation-title',
        ariaDescribedBy: 'group-confirmation-description',
      })
      .closed.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.saving.set(true);
        this.actionError.set('');
        request()
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            next: () => {
              this.saving.set(false);
              this.status.set(message);
              this.loadMembers(null);
              this.loadRequests(null);
            },
            error: (response: HttpErrorResponse) => {
              this.saving.set(false);
              this.actionError.set(
                response.error?.detail || 'No pudimos actualizar el grupo. Volvé a intentarlo.',
              );
              if (response.status === 404 || response.status === 409) {
                this.loadMembers(null);
                this.loadRequests(null);
              }
            },
          });
      });
  }
}
