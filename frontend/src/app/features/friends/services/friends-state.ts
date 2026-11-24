import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription, fromEvent } from 'rxjs';
import { AuthService } from '../../auth/services/auth';
import { createRateLimitCooldown } from '../../auth/rate-limit-cooldown';
import { FriendsSummaryDto } from '../models/friend';
import { FriendsService } from './friends';

@Injectable({ providedIn: 'root' })
export class FriendsState {
  private readonly auth = inject(AuthService);
  private readonly service = inject(FriendsService);
  private readonly account = computed(() => this.auth.currentUser()?.id ?? null);
  private request?: Subscription;
  readonly summary = signal<FriendsSummaryDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly cooldown = createRateLimitCooldown();
  readonly pendingReceived = computed(() => this.summary()?.pendingReceivedCount ?? 0);

  constructor() {
    effect(() => {
      const account = this.account();
      untracked(() => {
        this.request?.unsubscribe();
        this.summary.set(null);
        this.error.set(null);
        this.loading.set(false);
        if (account !== null) this.refresh();
      });
    });
    this.service.changes$.pipe(takeUntilDestroyed()).subscribe(() => this.refresh());
    fromEvent(window, 'focus')
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.refresh());
    inject(DestroyRef).onDestroy(() => this.request?.unsubscribe());
  }

  refresh(): void {
    if (this.account() === null || this.cooldown.remaining()) return;
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.request = this.service.summary().subscribe({
      next: (value) => {
        this.summary.set(value);
        this.loading.set(false);
      },
      error: (error) => {
        this.loading.set(false);
        this.error.set('No pudimos actualizar los contadores de amigos.');
        this.cooldown.handle(error);
      },
    });
  }
}
