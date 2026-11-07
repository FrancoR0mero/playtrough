import { RatingSummary } from '../../reviews/models/review';

export interface GameSummary {
  gameId?: number;
  sourceUrl?: string | null;
  externalId: number;
  name: string;
  slug: string;
  coverUrl: string | null;
  releaseYear: number | null;
  platforms: string[];
}

export interface GameSearchResponse {
  items: GameSummary[];
  page: number;
  pageSize: number;
  hasMore: boolean;
  isLimited?: boolean;
}

export interface GameDetail {
  sourceUrl?: string | null;
  metadataUnavailable?: boolean;
  providerRating?: number | null;
  providerRatingCount?: number | null;
  criticScore?: number | null;
  screenshots?: string[];
  ratingSummary: RatingSummary;
  id: number;
  externalId: number;
  name: string;
  slug: string;
  summary: string;
  coverUrl: string | null;
  coverUrlLarge: string | null;
  releaseDate: string | null;
  platforms: string[];
  genres: string[];
  developers: string[];
}
