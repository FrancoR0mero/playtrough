import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Subject } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AuthService } from '../../auth/services/auth';
import {
  ReactionChange,
  ReactionSummaryDto,
  ReactionTarget,
  ReactionValue,
} from '../models/reaction';

interface PendingReaction {
  key: string;
  target: ReactionTarget;
  confirmed: ReactionSummaryDto;
  desired: ReactionValue | null;
}

@Injectable({ providedIn: 'root' })
export class ReactionsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly pending = new Map<string, PendingReaction>();
  private readonly changes = new Subject<ReactionChange>();
  readonly changes$ = this.changes.asObservable();

  set(target: ReactionTarget, value: ReactionValue) {
    return this.http.put<ReactionSummaryDto>(this.url(target), { value });
  }

  remove(target: ReactionTarget) {
    return this.http.delete<ReactionSummaryDto>(this.url(target));
  }

  key(target: ReactionTarget): string {
    return `${this.auth.currentUser()?.id ?? 0}:${this.url(target)}`;
  }

  optimisticSummary(target: ReactionTarget): ReactionSummaryDto | undefined {
    const pending = this.pending.get(this.key(target));
    return pending && project(pending);
  }

  toggle(target: ReactionTarget, summary: ReactionSummaryDto, value: ReactionValue): void {
    const key = this.key(target);
    const pending = this.pending.get(key);
    if (pending) {
      pending.desired = pending.desired === value ? null : value;
      this.publishOptimistic(pending);
      return;
    }
    const operation: PendingReaction = {
      key,
      target,
      confirmed: summary,
      desired: summary.myReaction === value ? null : value,
    };
    this.pending.set(key, operation);
    this.publishOptimistic(operation);
    this.send(operation);
  }

  private send(operation: PendingReaction): void {
    const { key, target, desired: sent } = operation;
    const request = sent === null ? this.remove(target) : this.set(target, sent);
    request.subscribe({
      next: (summary) => {
        if (this.key(target) !== key) {
          this.pending.delete(key);
          return;
        }
        operation.confirmed = summary;
        if (operation.desired !== sent && operation.desired !== summary.myReaction) {
          this.publishOptimistic(operation);
          this.send(operation);
          return;
        }
        this.pending.delete(key);
        this.changes.next({ kind: 'saved', key, target, summary });
      },
      error: (error: unknown) => {
        this.pending.delete(key);
        if (this.key(target) === key)
          this.changes.next({ kind: 'failed', key, target, summary: operation.confirmed, error });
      },
    });
  }

  private publishOptimistic(operation: PendingReaction): void {
    this.changes.next({
      kind: 'optimistic',
      key: operation.key,
      target: operation.target,
      summary: project(operation),
    });
  }

  private url(target: ReactionTarget): string {
    const path =
      target.type === 'review'
        ? `games/${target.gameId}/reviews/${target.id}`
        : `timeline/${target.type === 'entry' ? 'entries' : 'comments'}/${target.id}`;
    return `${environment.apiUrl}/${path}/reaction`;
  }
}

function project({ confirmed, desired }: PendingReaction): ReactionSummaryDto {
  const count = (value: ReactionValue, total: number) =>
    Math.max(0, total + Number(desired === value) - Number(confirmed.myReaction === value));
  return {
    myReaction: desired,
    likeCount: count('like', confirmed.likeCount),
    dislikeCount: count('dislike', confirmed.dislikeCount),
  };
}
