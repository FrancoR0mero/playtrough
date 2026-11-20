import { TestBed } from '@angular/core/testing';
import { HubConnectionState } from '@microsoft/signalr';
import { of } from 'rxjs';
import { RealtimeConnection } from '../../core/realtime/realtime-connection';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { NotificationsApi } from './notifications-api';
import { NotificationsState } from './notifications-state';

describe('NotificationsState', () => {
  function connection() {
    const events = new Map<string, (id?: string) => void>();
    const hub = {
      state: HubConnectionState.Disconnected,
      on: vi.fn((type: string, callback: (id?: string) => void) => events.set(type, callback)),
      onreconnected: vi.fn<(callback: () => void) => void>(),
      onclose: vi.fn<(callback: () => void) => void>(),
      start: vi.fn().mockResolvedValue(undefined),
      stop: vi.fn().mockResolvedValue(undefined),
    };
    return { hub, events };
  }

  function setup() {
    const auth = createAuthMock();
    const first = connection();
    const second = connection();
    const create = vi.fn().mockReturnValueOnce(first.hub).mockReturnValue(second.hub);
    const count = vi.fn().mockReturnValue(of({ unreadCount: 7 }));
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: NotificationsApi, useValue: { count } },
        { provide: RealtimeConnection, useValue: { create } },
      ],
    });
    const state = TestBed.inject(NotificationsState);
    auth.currentUser.set(createSession().user);
    TestBed.tick();
    return { auth, first, second, create, count, state };
  }

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('uses server counts and refreshes on distinct events, reconnect, read and focus', async () => {
    const { count, create, first, state } = setup();
    expect(create).toHaveBeenCalledWith('/hubs/notifications');
    expect(state.unreadCount()).toBe(7);
    await vi.advanceTimersByTimeAsync(60);
    count.mockClear();

    count.mockReturnValue(of({ unreadCount: 2 }));
    first.events.get('NotificationAvailable')!('42');
    await vi.advanceTimersByTimeAsync(60);
    expect(state.unreadCount()).toBe(2);
    first.events.get('NotificationAvailable')!('42');
    await vi.advanceTimersByTimeAsync(60);
    expect(count).toHaveBeenCalledTimes(1);

    first.hub.onreconnected.mock.calls[0][0]();
    await vi.advanceTimersByTimeAsync(60);
    first.events.get('NotificationsRead')!();
    await vi.advanceTimersByTimeAsync(60);
    window.dispatchEvent(new Event('focus'));
    await vi.advanceTimersByTimeAsync(60);
    expect(count).toHaveBeenCalledTimes(4);
  });

  it('discards old connection events when changing the authenticated account', async () => {
    const { auth, count, first, second, state } = setup();
    await vi.advanceTimersByTimeAsync(60);
    count.mockReturnValue(of({ unreadCount: 1 }));
    auth.currentUser.set({ ...createSession().user, id: 2 });
    expect(state.unreadCount()).toBe(0);
    TestBed.tick();
    expect(first.hub.stop).toHaveBeenCalledOnce();
    expect(second.hub.start).toHaveBeenCalledOnce();
    expect(state.unreadCount()).toBe(1);
    await vi.advanceTimersByTimeAsync(60);
    count.mockClear();
    first.events.get('NotificationAvailable')!('99');
    await vi.advanceTimersByTimeAsync(60);
    expect(count).not.toHaveBeenCalled();
  });
});
