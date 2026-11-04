export interface RatingSummary {
  average: number | null;
  count: number;
}

export interface MyReviewDto {
  id: number;
  gameId: number;
  rating: number;
  text: string | null;
  createdAt: string;
  editedAt: string | null;
}

export type ReviewSort = 'recent' | 'top' | 'low';

export interface PublicReviewDto extends Omit<MyReviewDto, 'gameId'> {
  author: { userId: number; username: string };
  isMine: boolean;
}

export interface ReviewListResponse {
  items: PublicReviewDto[];
  ratingSummary: RatingSummary;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface SaveReviewResponse {
  review: MyReviewDto;
  ratingSummary: RatingSummary;
}

export interface DeleteReviewResponse {
  ratingSummary: RatingSummary;
}

export type ReviewChange =
  | ({ type: 'created' | 'updated'; gameId: number } & SaveReviewResponse)
  | ({ type: 'deleted'; gameId: number } & DeleteReviewResponse);
