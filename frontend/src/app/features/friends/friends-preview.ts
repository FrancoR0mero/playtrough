import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from '../profile/profile-error';
import { FriendDto } from './models/friend';
import { FriendsService } from './services/friends';
import { visibleSocialRefresh } from '../profile/social-refresh';
import { ProfileRestrictionComponent } from '../profile/profile-restriction';

@Component({
  selector: 'app-friends-preview',
  imports: [RouterLink, UserAvatarComponent, ProfileRestrictionComponent],
  template: `
    <section class="mt-8 border-t border-white/10 pt-6" aria-label="Amigos del jugador">
      <div class="flex items-center justify-between gap-4">
        <h2 class="text-lg font-semibold text-white">Amigos</h2>
        @if (!restricted()) {
          <a
            [routerLink]="['/profile', username(), 'friends']"
            class="inline-flex min-h-11 items-center text-sm text-slate-300 hover:underline"
            >Ver todos</a
          >
        }
      </div>
      @if (loading()) {
        <p role="status" class="mt-3 text-sm text-slate-400">Cargando amigos…</p>
      } @else if (!error() && !items().length) {
        <p class="mt-3 text-sm text-slate-400">Este jugador todavía no tiene amigos.</p>
      }
      <ul class="mt-3 flex flex-wrap gap-3">
        @for (friend of items(); track friend.user.username) {
          <li>
            <a
              [routerLink]="['/profile', friend.user.username]"
              [attr.aria-label]="'Ver perfil de ' + friend.user.username"
              [title]="friend.user.username"
              class="inline-flex min-h-11 min-w-11 items-center justify-center rounded-full"
            >
              <app-user-avatar
                [username]="friend.user.username"
                [avatarUrl]="friend.user.avatarUrl"
              />
            </a>
          </li>
        }
      </ul>
      @if (restricted()) {
        <app-profile-restriction />
      } @else if (cooldown.message() || error(); as message) {
        <p role="alert" class="mt-3 text-sm text-red-300">{{ message }}</p>
      }
      @if (error() && !restricted()) {
        <button
          type="button"
          (click)="load()"
          [disabled]="loading() || cooldown.remaining() > 0"
          class="ui-button mt-3 min-h-11 border border-slate-600 px-4 py-2 text-white disabled:opacity-50"
        >
          Reintentar
        </button>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FriendsPreviewComponent {
  readonly username = input.required<string>();
  readonly accessChanged = output<void>();
  private readonly friends = inject(FriendsService);
  private request?: Subscription;
  protected readonly items = signal<FriendDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly restricted = signal(false);
  protected readonly cooldown = createRateLimitCooldown();
  constructor() {
    visibleSocialRefresh()
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.load());
    effect(() => {
      this.username();
      untracked(() => this.load());
    });
    this.friends.changes$.pipe(takeUntilDestroyed()).subscribe(() => this.load());
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }
  protected load(): void {
    this.request?.unsubscribe();
    this.items.set([]);
    this.loading.set(false);
    if (this.cooldown.remaining()) return;
    this.loading.set(true);
    this.error.set(null);
    this.restricted.set(false);
    this.request = this.friends.forUser(this.username(), 1, 6).subscribe({
      next: (result) => {
        this.items.set(result.items.slice(0, 6));
        this.loading.set(false);
      },
      error: (error) => {
        this.loading.set(false);
        const restricted = profileError(error).code === 'profile_restricted';
        this.restricted.set(restricted);
        if (restricted || profileError(error).code === 'user_not_found') this.accessChanged.emit();
        this.error.set(
          restricted
            ? 'La lista de amigos de este perfil es privada.'
            : 'No pudimos cargar los amigos.',
        );
        this.cooldown.handle(error);
      },
    });
  }
}
