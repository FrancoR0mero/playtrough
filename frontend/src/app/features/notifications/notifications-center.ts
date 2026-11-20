import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  effect,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  auditTime,
  catchError,
  finalize,
  Observable,
  of,
  startWith,
  Subscription,
  switchMap,
} from 'rxjs';
import { NotificationItem, NotificationPage, NotificationsApi } from './notifications-api';
import { NotificationsState } from './notifications-state';

@Component({
  selector: 'app-notifications-center',
  imports: [DatePipe, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './notifications-center.html',
})
export class NotificationsCenterComponent {
  private readonly api = inject(NotificationsApi);
  private readonly state = inject(NotificationsState);
  private readonly destroyRef = inject(DestroyRef);
  private olderRequest?: Subscription;
  protected readonly rows = signal<NotificationItem[]>([]);
  protected readonly nextCursor = signal<string | null>(null);
  protected readonly cutoff = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadingOlder = signal(false);
  protected readonly error = signal(false);
  protected readonly olderError = signal(false);
  protected readonly actionError = signal(false);
  protected readonly pending = signal(false);
  protected readonly unread = this.state.unreadCount;

  constructor() {
    effect((onCleanup) => {
      const account = this.state.account();
      this.rows.set([]);
      this.nextCursor.set(null);
      this.cutoff.set(null);
      this.error.set(false);
      this.actionError.set(false);
      this.pending.set(false);
      this.loading.set(account !== null);
      if (account === null) return;

      const pages = this.state.changes
        .pipe(
          auditTime(50),
          startWith(undefined),
          switchMap(() => {
            this.olderRequest?.unsubscribe();
            this.loading.set(true);
            this.error.set(false);
            this.olderError.set(false);
            return this.api.list().pipe(catchError(() => of(null)));
          }),
        )
        .subscribe((page) => {
          if (this.state.account() !== account) return;
          this.loading.set(false);
          this.rows.set(page?.items ?? []);
          this.nextCursor.set(page?.nextCursor ?? null);
          this.cutoff.set(page?.readCutoff ?? null);
          this.error.set(page === null);
        });
      onCleanup(() => {
        pages.unsubscribe();
        this.olderRequest?.unsubscribe();
      });
    });
  }

  protected retry(): void {
    this.state.refresh();
  }

  protected loadOlder(): void {
    const cursor = this.nextCursor();
    const account = this.state.account();
    if (cursor === null || account === null || this.loading() || this.loadingOlder()) return;
    this.loadingOlder.set(true);
    this.olderError.set(false);
    this.olderRequest = this.api
      .list(cursor)
      .pipe(finalize(() => this.loadingOlder.set(false)))
      .subscribe({
        next: (page: NotificationPage) => {
          if (this.state.account() !== account) return;
          this.rows.update((rows) => [...rows, ...page.items]);
          this.nextCursor.set(page.nextCursor);
        },
        error: () => this.olderError.set(true),
      });
  }

  protected read(item: NotificationItem): void {
    if (item.readAt === null) this.mutate(this.api.read(item.id));
  }

  protected readAll(): void {
    const cutoff = this.cutoff();
    if (cutoff !== null && !this.loading()) this.mutate(this.api.readAll(cutoff));
  }

  private mutate(request: Observable<unknown>): void {
    const account = this.state.account();
    if (account === null || this.pending()) return;
    this.pending.set(true);
    this.actionError.set(false);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          if (this.state.account() === account) this.pending.set(false);
        }),
      )
      .subscribe({
        next: () => {
          if (this.state.account() === account) this.state.refresh();
        },
        error: () => {
          if (this.state.account() === account) this.actionError.set(true);
        },
      });
  }
}
