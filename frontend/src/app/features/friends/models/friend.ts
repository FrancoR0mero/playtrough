import { UserSummaryDto } from '../../profile/models/user';

export interface RelationshipDto {
  status: 'none' | 'friends' | 'request_sent' | 'request_received';
  requestId?: number;
}
export interface FriendsPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}
export interface FriendDto {
  user: UserSummaryDto;
  since: string;
}
export interface FriendRequestDto {
  id: number;
  user: UserSummaryDto;
  createdAt: string;
}
export interface SentFriendRequestDto extends FriendRequestDto {
  status: string;
}
export interface AcceptedFriendDto {
  friend: UserSummaryDto;
  since: string;
}
export interface FriendsSummaryDto {
  friendsCount: number;
  pendingReceivedCount: number;
  pendingSentCount: number;
}
export interface FriendshipChange {
  username: string;
  action: 'sent' | 'accepted' | 'rejected' | 'cancelled' | 'removed';
}
export const FRIENDSHIP_MESSAGES = {
  sent: 'Solicitud enviada',
  accepted: 'Solicitud aceptada',
  rejected: 'Solicitud rechazada',
  cancelled: 'Solicitud cancelada',
  removed: 'Amistad eliminada',
};
