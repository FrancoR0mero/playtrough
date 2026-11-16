import { LibraryStatus } from '../../library/models/library';

export type TimelineType = 'added' | 'status_changed' | 'reviewed';
export type TimelinePerspective = 'self' | 'other';
export interface TimelineEntryDto {
  id: number;
  type: TimelineType;
  occurredOn: string;
  recordedAt: string;
  game: {
    gameId: number;
    externalId: number;
    name: string;
    slug: string;
    coverUrl: string | null;
    releaseYear: number | null;
  };
  fromStatus: LibraryStatus | null;
  toStatus: LibraryStatus | null;
  review: { id: number; rating: number; excerpt: string | null } | null;
  isHidden: boolean;
}
export interface FeedEntryDto extends TimelineEntryDto {
  author: { username: string; avatarUrl: string | null };
}
export interface TimelinePage<T = TimelineEntryDto> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}
export interface TimelinePeriods {
  years: { year: number; count: number; months: { month: number; count: number }[] }[];
}
export interface TimelineQuery {
  cursor?: string;
  limit?: number;
  year?: number;
  month?: number;
  type?: readonly TimelineType[];
}
