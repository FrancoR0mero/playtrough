import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { normalizePlainText } from '../../shared/plain-text';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { feedTimestamp, fullFeedDate, relativeFeedTime } from '../feed/feed-text';
import { profileError } from '../profile/profile-error';
import { CommentAction, CommentMenuComponent } from './comment-menu';
import { CommentDto } from './models/comment';
import { TimelineService } from './services/timeline';

const MAX_LENGTH = 500;
const validLength = (length: number) => length > 0 && length <= MAX_LENGTH;

@Component({
  selector: 'app-entry-comments',
  imports: [RouterLink, ReactiveFormsModule, CommentMenuComponent, UserAvatarComponent],
  templateUrl: './entry-comments.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntryCommentsComponent implements OnInit {
  readonly entryId = input.required<number>();
  readonly count = input.required<number>();
  readonly countChanged = output<number>();
  readonly unavailable = output<void>();
  private readonly service = inject(TimelineService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly composer = viewChild<ElementRef<HTMLTextAreaElement>>('composer');
  private cursor: string | null = null;

  protected readonly form = new FormControl('', { nonNullable: true });
  protected readonly editForm = new FormControl('', { nonNullable: true });
  private readonly draft = toSignal(this.form.valueChanges, { initialValue: '' });
  private readonly editDraft = toSignal(this.editForm.valueChanges, { initialValue: '' });
  protected readonly length = computed(() => Array.from(normalizePlainText(this.draft())).length);
  protected readonly editLength = computed(
    () => Array.from(normalizePlainText(this.editDraft())).length,
  );
  protected readonly items = signal<CommentDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly listError = signal('');
  protected readonly formError = signal('');
  protected readonly rowError = signal('');
  protected readonly status = signal('');
  protected readonly pending = signal<'add' | CommentAction | null>(null);
  protected readonly active = signal<{ id: number; action: CommentAction } | null>(null);
  protected readonly writeCooldown = createRateLimitCooldown();
  protected readonly readCooldown = createRateLimitCooldown();
  protected readonly maxLength = MAX_LENGTH;
  protected readonly timestamp = feedTimestamp;
  protected readonly fullDate = fullFeedDate;
  protected readonly now = signal(Date.now());
  protected readonly relativeDate = (value: string) => relativeFeedTime(value, this.now());
  protected readonly canPost = computed(
    () =>
      validLength(this.length()) &&
      !this.pending() &&
      !this.loading() &&
      !this.writeCooldown.remaining(),
  );

  ngOnInit(): void {
    this.load();
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  focusComposer(): void {
    this.composer()?.nativeElement.focus();
  }

  protected load(): void {
    if (this.loading() || this.pending() || this.readCooldown.remaining()) return;
    this.loading.set(true);
    this.listError.set('');
    const cursor = this.cursor;
    this.service
      .getComments(this.entryId(), { limit: 20, ...(cursor ? { cursor } : {}) })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          const rows = new Map((cursor ? this.items() : []).map((item) => [item.id, item]));
          for (const item of page.items) if (!rows.has(item.id)) rows.set(item.id, item);
          this.items.set([...rows.values()]);
          this.countChanged.emit(page.commentCount);
          this.cursor = page.nextCursor;
          this.hasMore.set(page.hasMore && !!this.cursor && this.cursor !== cursor);
          this.loaded.set(true);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (error instanceof HttpErrorResponse && error.status === 404) {
            this.unavailable.emit();
            return;
          }
          this.readCooldown.handle(error);
          const invalidCursor = profileError(error).code === 'invalid_cursor';
          if (invalidCursor) {
            this.cursor = null;
          }
          this.listError.set(
            invalidCursor
              ? 'Volvé a cargar los comentarios.'
              : 'No pudimos cargar los comentarios.',
          );
        },
      });
  }

  protected shortcut(event: KeyboardEvent): void {
    if (event.key !== 'Enter' || !(event.ctrlKey || event.metaKey) || event.isComposing) return;
    event.preventDefault();
    this.post();
  }

  protected post(event?: Event): void {
    event?.preventDefault();
    if (!this.canPost()) return;
    this.pending.set('add');
    this.form.disable({ emitEvent: false });
    this.formError.set('');
    this.status.set('');
    this.service
      .addComment(this.entryId(), normalizePlainText(this.draft()))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ comment, commentCount }) => {
          this.items.update((items) => [
            comment,
            ...items.filter((item) => item.id !== comment.id),
          ]);
          this.countChanged.emit(commentCount);
          this.form.reset();
          this.form.enable({ emitEvent: false });
          this.pending.set(null);
          this.status.set('Comentario publicado');
          this.focusComposer();
        },
        error: (error: unknown) => {
          this.pending.set(null);
          this.form.enable({ emitEvent: false });
          if (error instanceof HttpErrorResponse && error.status === 404) this.unavailable.emit();
          else this.formError.set(this.writeError(error));
        },
      });
  }

  protected select(comment: CommentDto, action: CommentAction): void {
    if (this.pending() || (action === 'edit' ? !comment.isMine : !comment.canDelete)) return;
    this.rowError.set('');
    this.editForm.setValue(action === 'edit' ? comment.text : '');
    this.active.set({ id: comment.id, action });
    this.focusRow(comment.id, action === 'edit' ? '[data-comment-edit]' : '[data-comment-cancel]');
  }

  protected cancel(comment: CommentDto, event?: Event): void {
    if (this.pending()) return;
    event?.preventDefault();
    this.active.set(null);
    this.rowError.set('');
    this.focusRow(comment.id, '[data-comment-menu]');
  }

  protected canSave(comment: CommentDto): boolean {
    return (
      !this.pending() &&
      !this.loading() &&
      !this.writeCooldown.remaining() &&
      validLength(this.editLength()) &&
      normalizePlainText(this.editDraft()) !== comment.text
    );
  }

  protected save(comment: CommentDto, event: Event): void {
    event.preventDefault();
    if (!comment.isMine || !this.canSave(comment)) return;
    this.prepareWrite('edit');
    this.service
      .editComment(comment.id, normalizePlainText(this.editDraft()))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.pending.set(null);
          this.items.update((items) =>
            items.map((item) => (item.id === updated.id ? updated : item)),
          );
          this.cancel(comment);
          this.status.set('Comentario actualizado');
        },
        error: (error: unknown) => this.rowFailed(comment, error),
      });
  }

  protected remove(comment: CommentDto): void {
    if (!comment.canDelete || this.pending() || this.loading() || this.writeCooldown.remaining())
      return;
    this.prepareWrite('delete');
    this.service
      .deleteComment(comment.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ commentCount }) => this.removed(comment, commentCount),
        error: (error: unknown) => this.rowFailed(comment, error),
      });
  }

  private prepareWrite(action: CommentAction): void {
    this.pending.set(action);
    this.rowError.set('');
    this.status.set('');
  }

  private rowFailed(comment: CommentDto, error: unknown): void {
    this.pending.set(null);
    if (error instanceof HttpErrorResponse && error.status === 404) {
      if (profileError(error).code === 'entry_not_found') this.unavailable.emit();
      else {
        this.removed(comment, Math.max(0, this.count() - 1));
        // Reconcile the count and verify entry access when the item disappeared elsewhere.
        this.cursor = null;
        this.load();
      }
    } else this.rowError.set(this.writeError(error));
  }

  private removed(comment: CommentDto, count: number): void {
    this.pending.set(null);
    const items = this.items();
    const next = items[items.findIndex((item) => item.id === comment.id) + 1];
    this.items.set(items.filter((item) => item.id !== comment.id));
    this.active.set(null);
    this.countChanged.emit(count);
    this.status.set('Comentario eliminado');
    if (next) this.focusRow(next.id);
    else afterNextRender(() => this.focusComposer(), { injector: this.injector });
  }

  private focusRow(id: number, selector?: string): void {
    afterNextRender(
      () => {
        const row = this.host.nativeElement.querySelector<HTMLElement>(`[data-comment-id="${id}"]`);
        const target = selector ? row?.querySelector<HTMLElement>(selector) : row;
        if (target) target.focus();
        else this.focusComposer();
      },
      { injector: this.injector },
    );
  }

  private writeError(error: unknown): string {
    this.writeCooldown.handle(error);
    switch (profileError(error).code) {
      case 'invalid_comment':
        return 'Escribí un comentario de hasta 500 caracteres.';
      case 'duplicate_comment':
        return 'Ya publicaste este comentario hace un momento';
      default:
        return 'No pudimos completar la acción. Volvé a intentarlo.';
    }
  }
}
