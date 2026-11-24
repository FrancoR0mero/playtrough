import { DOCUMENT } from '@angular/common';
import { computed, effect, inject, Injectable, signal } from '@angular/core';
import { HubConnectionState } from '@microsoft/signalr';
import {
  auditTime,
  catchError,
  EMPTY,
  filter,
  fromEvent,
  merge,
  startWith,
  Subject,
  switchMap,
} from 'rxjs';
import { RealtimeConnection } from '../../core/realtime/realtime-connection';
import { AuthService } from '../auth/services/auth';
import { NotificationsApi } from './notifications-api';

@Injectable({ providedIn: 'root' })
export class NotificationsState {
  private readonly auth = inject(AuthService);
  private readonly api = inject(NotificationsApi);
  private readonly realtime = inject(RealtimeConnection);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly invalidations = new Subject<number>();
  private readonly count = signal<{ account: number; value: number } | null>(null);
  readonly account = computed(() => this.auth.currentUser()?.id ?? null);
  readonly unreadCount = computed(() => {
    const count = this.count();
    return count?.account === this.account() ? count.value : 0;
  });
  readonly changes = this.invalidations.pipe(filter((account) => account === this.account()));

  constructor() {
    effect((onCleanup) => {
      const account = this.account();
      this.count.set(null);
      if (account === null || !this.window) return;

      const hub = this.realtime.create('/hubs/notifications');
      const seen = new Set<string>();
      let active = true;
      let retry: ReturnType<typeof setTimeout> | undefined;
      const current = () => active && this.account() === account;
      const invalidate = () => {
        if (current()) this.invalidations.next(account);
      };
      const schedule = () => {
        if (current() && retry === undefined) retry = setTimeout(connect, 5_000);
      };
      const connect = () => {
        clearTimeout(retry);
        retry = undefined;
        if (!current() || hub.state !== HubConnectionState.Disconnected) return;
        void hub.start().then(invalidate, schedule);
      };
      const countRequest = this.changes
        .pipe(
          auditTime(50),
          startWith(account),
          switchMap(() => this.api.count().pipe(catchError(() => EMPTY))),
        )
        .subscribe(({ unreadCount }) => {
          if (current()) this.count.set({ account, value: unreadCount });
        });
      const foreground = merge(
        fromEvent(this.window, 'focus'),
        fromEvent(this.window, 'online'),
      ).subscribe(() => {
        invalidate();
        if (retry !== undefined) connect();
      });

      hub.on('NotificationAvailable', (id: string) => {
        if (!current() || seen.has(id)) return;
        if (seen.size >= 200) seen.delete(seen.values().next().value!);
        seen.add(id);
        invalidate();
      });
      hub.on('NotificationsRead', invalidate);
      hub.onreconnected(invalidate);
      hub.onclose(schedule);
      connect();
      onCleanup(() => {
        active = false;
        clearTimeout(retry);
        countRequest.unsubscribe();
        foreground.unsubscribe();
        void hub.stop().catch(() => undefined);
      });
    });
  }

  refresh(): void {
    const account = this.account();
    if (account !== null) this.invalidations.next(account);
  }
}
