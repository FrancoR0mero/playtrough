import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from '../profile/profile-error';
import { EntryLikeDto } from './models/timeline';
import { TimelineService } from './services/timeline';

export interface EntryLikesDialogData {
  entryId: number;
}

@Component({
  selector: 'app-entry-likes-dialog',
  imports: [RouterLink, UserAvatarComponent],
  templateUrl: './entry-likes-dialog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntryLikesDialogComponent implements OnInit {
  private readonly entryId = inject<EntryLikesDialogData>(DIALOG_DATA).entryId;
  private readonly dialog = inject(DialogRef<void>);
  private readonly timeline = inject(TimelineService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private cursor: string | null = null;
  protected readonly items = signal<EntryLikeDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly unavailable = signal(false);
  protected readonly error = signal('');
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
        if (event instanceof NavigationStart) {
          this.dialog.config.restoreFocus = false;
          this.close();
        }
      });
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(trigger?: HTMLButtonElement): void {
    if (this.loading() || this.unavailable() || this.cooldown.remaining()) return;
    const focusOrigin = trigger?.ownerDocument.activeElement === trigger ? trigger : undefined;
    this.loading.set(true);
    this.error.set('');
    const cursor = this.cursor;
    this.timeline
      .getLikes(this.entryId, { limit: 20, ...(cursor ? { cursor } : {}) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          const rows = new Map(
            (cursor ? this.items() : []).map((item) => [item.user.username, item]),
          );
          for (const item of page.items)
            if (!rows.has(item.user.username)) rows.set(item.user.username, item);
          this.items.set([...rows.values()]);
          this.cursor = page.nextCursor;
          this.hasMore.set(page.hasMore && !!page.nextCursor && cursor !== page.nextCursor);
          this.loading.set(false);
          this.restorePaginationFocus(focusOrigin);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.items.set([]);
            this.hasMore.set(false);
            this.unavailable.set(true);
            this.restorePaginationFocus(focusOrigin);
            return;
          }
          this.cooldown.handle(error);
          const invalidCursor = profileError(error).code === 'invalid_cursor';
          if (invalidCursor) this.cursor = null;
          this.error.set(
            invalidCursor ? 'Volvé a cargar los Me gusta.' : 'No pudimos cargar los Me gusta.',
          );
          this.restorePaginationFocus(focusOrigin);
        },
      });
  }

  protected close(): void {
    this.dialog.close();
  }

  private restorePaginationFocus(origin?: HTMLButtonElement): void {
    if (!origin) return;
    afterNextRender(
      () => {
        const document = origin.ownerDocument;
        if (document.activeElement !== document.body) return;
        const panel = this.host.nativeElement;
        const target =
          origin.isConnected && !origin.disabled
            ? origin
            : (panel.querySelector<HTMLButtonElement>('[data-likes-retry]:not(:disabled)') ??
              panel.querySelector<HTMLButtonElement>('[data-close-likes]'));
        target?.focus();
      },
      { injector: this.injector },
    );
  }
}
