import { MyProfileDto, PrivacyDto, ProfileDto } from './models/user';

export const DEFAULT_PRIVACY: PrivacyDto = {
  profileVisibility: 'public',
  historyVisibility: 'public',
};

export const MY_PROFILE: MyProfileDto = {
  id: 1,
  username: 'ana',
  email: 'ana@example.com',
  avatarUrl: null,
  bio: null,
  createdAt: '2026-03-10T12:00:00Z',
  friendsCount: 2,
  pendingReceivedCount: 0,
  usernameChangeAvailableAt: null,
};

export const OTHER_PROFILE: ProfileDto = {
  username: 'jugador',
  avatarUrl: null,
  bio: 'Hola',
  createdAt: MY_PROFILE.createdAt,
  friendsCount: 3,
  relationship: { status: 'none' },
  isRestricted: false,
  restriction: null,
};
