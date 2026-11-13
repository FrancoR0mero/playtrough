import { signal } from '@angular/core';
import { Subject, of } from 'rxjs';
import { FriendshipChange } from './models/friend';

export const FRIEND = { user: { username: 'bea', avatarUrl: null }, since: '2026-03-15T12:00:00Z' };
export const REQUEST = { id: 7, user: FRIEND.user, createdAt: FRIEND.since };
export const SUMMARY = { friendsCount: 2, pendingReceivedCount: 3, pendingSentCount: 1 };
export const EMPTY_FRIENDS = { items: [], page: 1, pageSize: 20, hasMore: false };
export function createFriendsMock() {
  const changes = new Subject<FriendshipChange>();
  return {
    changes,
    changes$: changes.asObservable(),
    notifyBlockChange: vi.fn((username: string, action: 'blocked' | 'unblocked') =>
      changes.next({ username, action }),
    ),
    summary: vi.fn().mockReturnValue(of(SUMMARY)),
    list: vi.fn().mockReturnValue(of(EMPTY_FRIENDS)),
    received: vi.fn().mockReturnValue(of(EMPTY_FRIENDS)),
    sent: vi.fn().mockReturnValue(of(EMPTY_FRIENDS)),
    forUser: vi.fn().mockReturnValue(of(EMPTY_FRIENDS)),
    send: vi.fn().mockReturnValue(of({ ...REQUEST, status: 'pending' })),
    accept: vi.fn().mockReturnValue(of({ friend: FRIEND.user, since: FRIEND.since })),
    reject: vi.fn().mockReturnValue(of(undefined)),
    cancel: vi.fn().mockReturnValue(of(undefined)),
    remove: vi.fn().mockReturnValue(of(undefined)),
  };
}
export function createFriendsStateMock() {
  return {
    summary: signal(SUMMARY),
    pendingReceived: signal(3),
    loading: signal(false),
    error: signal<string | null>(null),
    cooldown: { remaining: signal(0), message: signal<string | null>(null) },
    refresh: vi.fn(),
  };
}
