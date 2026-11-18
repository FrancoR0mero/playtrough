import { ReactionSummaryDto } from '../../reactions/models/reaction';
import { FeedEntryDto, TimelinePage } from './timeline';

export interface CommentDto extends ReactionSummaryDto {
  id: number;
  author: FeedEntryDto['author'];
  text: string;
  createdAt: string;
  editedAt: string | null;
  isMine: boolean;
  canDelete: boolean;
}

export interface CommentCountDto {
  commentCount: number;
}

export type CommentPage = TimelinePage<CommentDto> & CommentCountDto;
export type CommentCreatedDto = { comment: CommentDto } & CommentCountDto;
