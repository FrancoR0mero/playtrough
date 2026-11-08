import { RelationshipDto } from '../../friends/models/friend';

export interface UserSummaryDto {
  username: string;
  avatarUrl: string | null;
}

export interface UserSearchDto extends UserSummaryDto {
  relationship: RelationshipDto;
}

export interface UserSearchPage {
  items: UserSearchDto[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export interface MyProfileDto extends UserSummaryDto {
  id: number;
  email: string;
  bio: string | null;
  createdAt: string;
  friendsCount: number;
  pendingReceivedCount: number;
  usernameChangeAvailableAt: string | null;
}

export interface ProfileDto extends UserSummaryDto {
  bio?: string | null;
  createdAt?: string | null;
  friendsCount?: number | null;
  relationship: RelationshipDto;
  isRestricted: boolean;
  restriction: 'private' | 'friends_only' | null;
}

export interface UpdateProfileRequest {
  username?: string;
  bio?: string | null;
}
