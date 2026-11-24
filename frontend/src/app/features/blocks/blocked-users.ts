import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileDate } from '../profile/profile-error';
import { visibleSocialRefresh } from '../profile/social-refresh';
import { BlockedUser, BlocksService } from './services/blocks';

@Component({
  selector: 'app-blocked-users',
  imports: [RouterLink, UserAvatarComponent],
  templateUrl: './blocked-users.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BlockedUsersComponent {
  private readonly blocks = inject(BlocksService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly route = inject(ActivatedRoute);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private request?: Subscription;
  private page = 0;
  private failedPage = 1;
  protected readonly items = signal<BlockedUser[]>([]);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly busy = signal<string | null>(null);
  protected readonly confirming = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal(
    this.route.snapshot.queryParamMap.get('done') === 'blocked'
      ? 'Usuario bloqueado. Podés administrarlo desde esta lista.'
      : '',
  );
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly date = profileDate;
  constructor() {
    this.load();
    afterNextRender(() => this.heading()?.nativeElement.focus());
    visibleSocialRefresh()
      .pipe(takeUntilDestroyed())
      .subscribe(() => {
        if (!this.busy() && !this.confirming()) this.load();
      });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }
  protected load(page = 1): void {
    if (this.loading() || this.cooldown.remaining()) return;
    this.request?.unsubscribe();
    this.failedPage = page;
    this.loading.set(true);
    this.error.set(null);
    if (page === 1) {
      this.items.set([]);
      this.hasMore.set(false);
    }
    this.request = this.blocks
      .list(page)
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (result) => {
          const rows = page === 1 ? [] : this.items();
          this.items.set([
            ...new Map(
              [...rows, ...result.items].map((r) => [r.user.username.toLowerCase(), r]),
            ).values(),
          ]);
          this.page = result.page;
          this.hasMore.set(result.hasMore);
          this.loaded.set(true);
        },
        error: (error) => {
          this.cooldown.handle(error);
          this.error.set(
            'No pudimos cargar los usuarios bloqueados. Revisá tu conexión e intentá de nuevo.',
          );
        },
      });
  }
  protected more(): void {
    if (this.hasMore() && !this.busy()) this.load(this.page + 1);
  }
  protected retry(): void {
    this.load(this.failedPage);
  }
  protected unblock(row: BlockedUser): void {
    if (
      this.busy() ||
      this.loading() ||
      this.confirming() !== row.user.username ||
      this.cooldown.remaining()
    )
      return;
    this.busy.set(row.user.username);
    this.error.set(null);
    this.status.set('');
    this.blocks
      .unblock(row.user.username)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(null)),
      )
      .subscribe({
        next: () => {
          this.items.update((rows) => rows.filter((r) => r.user.username !== row.user.username));
          this.confirming.set(null);
          this.status.set('Usuario desbloqueado. La amistad no se restaura automáticamente.');
          this.heading()?.nativeElement.focus();
          this.load();
        },
        error: (error) => {
          this.cooldown.handle(error);
          this.error.set(
            'No pudimos desbloquear al usuario. El bloqueo sigue activo; volvé a intentar.',
          );
        },
      });
  }
}
