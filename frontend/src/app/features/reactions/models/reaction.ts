export type ReactionValue = 'like' | 'dislike';

export type ReactionTarget =
  { type: 'entry' | 'comment'; id: number } | { type: 'review'; id: number; gameId: number };

export interface ReactionSummaryDto {
  myReaction: ReactionValue | null;
  likeCount: number;
  dislikeCount: number;
}

export type ReactionChange = {
  key: string;
  target: ReactionTarget;
  summary: ReactionSummaryDto;
} & ({ kind: 'optimistic' | 'saved' } | { kind: 'failed'; error: unknown });
