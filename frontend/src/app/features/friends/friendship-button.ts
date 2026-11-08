import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  viewChild,
  computed,
  inject,
  input,
  linkedSignal,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize, map } from 'rxjs';
import { AuthService } from '../auth/services/auth';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileDate, profileError } from '../profile/profile-error';
import { UsersService } from '../profile/services/users';
import { FRIENDSHIP_MESSAGES, FriendshipChange, RelationshipDto } from './models/friend';
import { FriendsService } from './services/friends';

@Component({
  selector: 'app-friendship-button',
  templateUrl: './friendship-button.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FriendshipButtonComponent {
  readonly username = input.required<string>();
  readonly relationship = input.required<RelationshipDto>();
  readonly relationshipChanged = output<RelationshipDto>();
  private readonly auth = inject(AuthService);
  private readonly friends = inject(FriendsService);
  private readonly users = inject(UsersService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly removeButton = viewChild<ElementRef<HTMLButtonElement>>('removeButton');
  protected readonly state = linkedSignal(() => ({
    ...this.relationship(),
    username: this.username(),
  }));
  protected readonly own = computed(
    () => this.username().toLowerCase() === this.auth.currentUser()?.username.toLowerCase(),
  );
  protected readonly busy = signal(false);
  protected readonly confirm = signal(false);
  protected readonly status = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly needsSync = signal(false);
  protected readonly cooldown = createRateLimitCooldown();

  protected keepFriend(): void {
    this.confirm.set(false);
    this.removeButton()?.nativeElement.focus();
  }

  protected act(action: FriendshipChange['action']): void {
    if (this.own() || this.busy() || this.cooldown.remaining()) return;
    const id = this.state().requestId;
    if (['accepted', 'rejected', 'cancelled'].includes(action) && !id) {
      this.sync();
      return;
    }
    if (action === 'removed' && !this.confirm()) return;
    const username = this.username();
    let request: Observable<RelationshipDto>;
    switch (action) {
      case 'sent':
        request = this.friends
          .send(username)
          .pipe(map((result) => ({ status: 'request_sent', requestId: result.id })));
        break;
      case 'accepted':
        request = this.friends.accept(id!).pipe(map(() => ({ status: 'friends' })));
        break;
      case 'rejected':
        request = this.friends.reject(id!, username).pipe(map(() => ({ status: 'none' })));
        break;
      case 'cancelled':
        request = this.friends.cancel(id!, username).pipe(map(() => ({ status: 'none' })));
        break;
      case 'removed':
        request = this.friends.remove(username).pipe(map(() => ({ status: 'none' })));
        break;
    }
    this.busy.set(true);
    this.error.set(null);
    this.status.set(null);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (relationship) => {
        this.busy.set(false);
        this.confirm.set(false);
        this.needsSync.set(false);
        this.status.set(FRIENDSHIP_MESSAGES[action]);
        this.update(relationship);
      },
      error: (error) => {
        this.busy.set(false);
        this.handleError(error);
      },
    });
  }

  private update(relationship: RelationshipDto): void {
    this.state.set({ ...relationship, username: this.username() });
    this.relationshipChanged.emit(relationship);
  }

  protected sync(): void {
    if (this.busy() || this.cooldown.remaining()) return;
    this.busy.set(true);
    this.needsSync.set(true);
    this.users
      .getProfile(this.username())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (profile) => {
          this.needsSync.set(false);
          this.update(profile.relationship);
        },
        error: (error) => {
          if (!this.cooldown.handle(error))
            this.error.set('No pudimos actualizar la amistad. Revisá tu conexión y reintentá.');
        },
      });
  }

  private handleError(error: unknown): void {
    if (this.cooldown.handle(error)) return;
    const problem = profileError(error);
    const messages: Record<string, string> = {
      already_friends: 'Ya son amigos. Actualizamos el estado.',
      request_already_sent: 'Ya enviaste una solicitud. Actualizamos el estado.',
      request_already_received: 'Este jugador ya te envió una solicitud.',
      request_not_pending: 'La solicitud ya no está pendiente. Actualizamos el estado.',
      request_not_found: 'La solicitud ya no está disponible. Actualizamos el estado.',
      too_many_pending_requests:
        'Tenés demasiadas solicitudes enviadas pendientes. Cancelá alguna antes de enviar otra.',
      user_not_found: 'No encontramos a este jugador.',
      cannot_befriend_self: 'No podés agregarte como amigo.',
      cooldown_active: problem.availableAt
        ? `Podrás enviar otra solicitud el ${profileDate(problem.availableAt)}`
        : 'Todavía no podés enviar otra solicitud a este jugador.',
    };
    this.error.set(
      messages[problem.code ?? ''] ??
        'No pudimos completar la acción. Revisá tu conexión e intentá de nuevo.',
    );
    if (problem.code === 'request_already_received') {
      const body = error instanceof HttpErrorResponse ? error.error : null;
      const id = body?.extensions?.requestId ?? body?.requestId;
      if (Number.isSafeInteger(id) && id > 0) {
        this.update({ status: 'request_received', requestId: id });
        return;
      }
    }
    if (
      [
        'already_friends',
        'request_already_sent',
        'request_already_received',
        'request_not_pending',
        'request_not_found',
      ].includes(problem.code ?? '')
    )
      this.sync();
  }
}
