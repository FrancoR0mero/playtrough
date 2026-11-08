export interface UserSummaryDto {
  username: string;
  avatarUrl: string | null;
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
  relationship: {
    status: 'none' | 'friends' | 'request_sent' | 'request_received';
    requestId?: number;
  };
  isRestricted: boolean;
  restriction: 'private' | 'friends_only' | null;
}

export interface UpdateProfileRequest {
  username?: string;
  bio?: string | null;
}
