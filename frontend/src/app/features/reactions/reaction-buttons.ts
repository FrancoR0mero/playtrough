import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from '../profile/profile-error';
import {
  ReactionChange,
  ReactionSummaryDto,
  ReactionTarget,
  ReactionValue,
} from './models/reaction';
import { ReactionsService } from './services/reactions';

const compactNumber = new Intl.NumberFormat('es', { notation: 'compact' });
const labels = { like: 'Me gusta', dislike: 'No me gusta' };
const content = { entry: 'esta entrada', comment: 'este comentario', review: 'esta reseña' };
let nextId = 0;

@Component({
  selector: 'app-reaction-buttons',
  templateUrl: './reaction-buttons.html',
  host: { class: 'block min-w-0' },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReactionButtonsComponent {
  readonly target = input.required<ReactionTarget>();
  readonly summary = input.required<ReactionSummaryDto>();
  readonly isOwn = input(false);
  readonly compact = input(false);
  readonly showLikeList = input(false);
  readonly unavailable = output<ReactionTarget>();
  readonly viewLikes = output<HTMLButtonElement>();

  private readonly reactions = inject(ReactionsService);
  private readonly changes = signal<{ base: ReactionSummaryDto; change: ReactionChange } | null>(
    null,
  );
  private readonly key = computed(() => this.reactions.key(this.target()));
  private readonly last = computed(() => {
    const change = this.changes()?.change;
    return change?.key === this.key() ? change : null;
  });
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly choices = ['like', 'dislike'] as const;
  protected readonly explanationId = `reaction-ownership-${++nextId}`;
  protected readonly ownMessage = 'No podés reaccionar a tu propio contenido';
  protected readonly optimistic = computed(() => {
    // A change for this target invalidates the snapshot of the in-flight operation.
    this.last();
    return this.reactions.optimisticSummary(this.target());
  });
  protected readonly state = computed(() => {
    const base = this.summary();
    const last = this.last();
    return this.optimistic() ?? (last && this.changes()?.base === base ? last.summary : base);
  });
  protected readonly canListLikes = computed(
    () => this.showLikeList() && this.target().type === 'entry' && this.state().likeCount > 0,
  );
  private readonly failure = computed(() => {
    const last = this.last();
    return last?.kind === 'failed' ? last : null;
  });
  protected readonly rejectedOwn = computed(
    () => profileError(this.failure()?.error).code === 'cannot_react_to_own_content',
  );
  protected readonly blocked = computed(
    () =>
      this.isOwn() ||
      this.rejectedOwn() ||
      this.isMissing(this.failure()?.error) ||
      this.cooldown.remaining() > 0,
  );
  protected readonly error = computed(() => {
    const failure = this.failure();
    if (!failure) return null;
    if (this.rejectedOwn()) return this.ownMessage;
    if (this.isMissing(failure.error)) return 'Este contenido ya no está disponible';
    if (failure.error instanceof HttpErrorResponse && failure.error.status === 429)
      return this.cooldown.message();
    return 'No pudimos guardar tu reacción. Volvé a intentarlo.';
  });
  protected readonly announcement = computed(() => {
    const last = this.last();
    if (last?.kind !== 'saved') return '';
    const value = last.summary.myReaction;
    if (value === null) return 'Quitaste tu reacción';
    return `${value === 'like' ? 'Te gusta' : 'No te gusta'} ${content[this.target().type]}`;
  });

  constructor() {
    this.reactions.changes$.pipe(takeUntilDestroyed()).subscribe((change) => {
      if (change.key !== this.key()) return;
      this.changes.set({ base: this.summary(), change });
      if (change.kind !== 'failed') return;
      if (change.error instanceof HttpErrorResponse && change.error.status === 429)
        this.cooldown.handle(new RateLimitError(change.error));
      if (this.isMissing(change.error)) this.unavailable.emit(change.target);
    });
  }

  protected choose(value: ReactionValue): void {
    if (!this.blocked()) this.reactions.toggle(this.target(), this.state(), value);
  }

  protected label(value: ReactionValue): string {
    return `${labels[value]} (${this.count(value)})`;
  }

  protected formatted(value: ReactionValue): string {
    return compactNumber.format(this.count(value));
  }

  private count(value: ReactionValue) {
    return value === 'like' ? this.state().likeCount : this.state().dislikeCount;
  }

  private isMissing(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 404;
  }
}
