import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of, tap, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { OTHER_PROFILE } from '../profile/profile-test-helpers';
import { UsersService } from '../profile/services/users';
import { FriendsComponent } from './friends';
import {
  EMPTY_FRIENDS,
  FRIEND,
  REQUEST,
  createFriendsMock,
  createFriendsStateMock,
} from './friends-test-helpers';
import { FriendsService } from './services/friends';
import { FriendsState } from './services/friends-state';

describe('Friends lists', () => {
  let friends: ReturnType<typeof createFriendsMock>;
  let state: ReturnType<typeof createFriendsStateMock>;
  let harness: RouterTestingHarness;
  const root = () => harness.routeNativeElement!;
  const click = (name: string) => {
    [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === name)!.click();
    harness.detectChanges();
  };
  const page = (items: unknown[], index = 1, hasMore = false) => ({
    items,
    page: index,
    pageSize: 20,
    hasMore,
  });
  beforeEach(() => {
    friends = createFriendsMock();
    state = createFriendsStateMock();
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'friends', component: FriendsComponent },
          { path: 'profile/:username/friends', component: FriendsComponent },
        ]),
        { provide: FriendsService, useValue: friends },
        { provide: FriendsState, useValue: state },
        { provide: AuthService, useValue: auth },
        { provide: UsersService, useValue: { getProfile: () => of(OTHER_PROFILE) } },
      ],
    });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  async function start(url = '/friends') {
    harness = await RouterTestingHarness.create(url);
    harness.detectChanges();
  }
  it('restores URL tabs and query, exposes counters and navigates tabs by keyboard', async () => {
    await start('/friends?tab=received&q=bea');
    expect(friends.received).toHaveBeenCalledWith(1);
    expect(state.refresh).toHaveBeenCalled();
    const tabs = root().querySelectorAll<HTMLButtonElement>('[role="tab"]');
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(tabs[1].textContent).toContain('3');
    tabs[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toContain('tab=sent');
    expect(document.activeElement).toBe(tabs[2]);
    tabs[2].dispatchEvent(new KeyboardEvent('keydown', { key: 'Home', bubbles: true }));
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(friends.list).toHaveBeenLastCalledWith('bea', 1);
    expect(root().querySelector<HTMLInputElement>('input')!.value).toBe('bea');
  });
  it('loads more once and merges duplicate users case-insensitively', async () => {
    friends.list.mockReturnValue(of(page([FRIEND], 1, true)));
    await start();
    const pending = new Subject();
    friends.list.mockReturnValue(pending);
    click('Cargar más');
    click('Cargar más');
    expect(friends.list).toHaveBeenCalledTimes(2);
    pending.next(
      page(
        [
          { ...FRIEND, user: { username: 'BEA', avatarUrl: null } },
          { ...FRIEND, user: { username: 'cami', avatarUrl: null } },
        ],
        2,
      ),
    );
    pending.complete();
    harness.detectChanges();
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(2);
    expect(root().textContent).not.toContain('Cargar más');
  });
  it('debounces search, updates q and cancels old requests immediately', async () => {
    await start();
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const pending = new Subject();
    const unsub = vi.fn();
    friends.list.mockReturnValue(pending.pipe(tap({ unsubscribe: unsub })));
    // Start a request by navigating to a restored query, then type before it completes.
    await harness.navigateByUrl('/friends?q=old');
    const input = root().querySelector<HTMLInputElement>('input')!;
    input.value = 'bea';
    input.dispatchEvent(new Event('input'));
    harness.detectChanges();
    expect(unsub).toHaveBeenCalledOnce();
    friends.list.mockReturnValue(of(page([FRIEND])));
    await vi.advanceTimersByTimeAsync(399);
    expect(TestBed.inject(Router).url).toContain('q=old');
    await vi.advanceTimersByTimeAsync(1);
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toContain('q=bea');
    expect(friends.list).toHaveBeenLastCalledWith('bea', 1);
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(1);
  });
  it.each([
    ['received', 'Aceptar', 'accept', 'accepted'],
    ['received', 'Rechazar', 'reject', 'rejected'],
    ['sent', 'Cancelar', 'cancel', 'cancelled'],
  ] as const)(
    '%s: %s removes the row and announces the local action',
    async (tab, label, method, action) => {
      friends[tab].mockReturnValue(of(page([REQUEST])));
      await start(`/friends?tab=${tab}`);
      friends[tab].mockReturnValue(of(EMPTY_FRIENDS));
      friends[method].mockImplementation(() =>
        of(method === 'accept' ? { friend: FRIEND.user, since: FRIEND.since } : undefined).pipe(
          tap(() => {
            state.pendingReceived.set(0);
            state.summary.set({ ...state.summary(), pendingReceivedCount: 0, pendingSentCount: 0 });
            friends.changes.next({ username: 'bea', action });
          }),
        ),
      );
      click(label);
      harness.detectChanges();
      expect(root().querySelectorAll('app-friend-row')).toHaveLength(0);
      expect(root().querySelector('[role="status"]')!.textContent?.trim()).not.toBe('');
      expect(root().textContent).toContain(
        tab === 'received'
          ? 'No tenés solicitudes pendientes'
          : 'No tenés solicitudes enviadas pendientes',
      );
      expect(state.pendingReceived()).toBe(0);
    },
  );
  it('reloads when an action originates from another mounted component', async () => {
    await start();
    friends.list.mockReturnValue(of(page([FRIEND])));
    friends.changes.next({ username: 'bea', action: 'accepted' });
    harness.detectChanges();
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(1);
    expect(root().textContent).toContain('Solicitud aceptada');
  });
  it.each([
    ['friends', 'Todavía no tenés amigos'],
    ['received', 'No tenés solicitudes pendientes'],
    ['sent', 'No tenés solicitudes enviadas pendientes'],
  ])('shows the %s empty state', async (tab, text) => {
    await start(`/friends?tab=${tab}`);
    expect(root().textContent).toContain(text);
  });
  it('shows loading, preserves rows on page errors and retries the failed page', async () => {
    friends.list.mockReturnValue(of(page([FRIEND], 1, true)));
    await start();
    const pending = new Subject();
    friends.list.mockReturnValue(pending);
    click('Cargar más');
    expect(root().querySelector('[aria-label="Cargando jugadores"]')).not.toBeNull();
    pending.error(new HttpErrorResponse({ status: 0 }));
    harness.detectChanges();
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(1);
    expect(root().querySelector('[role="alert"]')).not.toBeNull();
    friends.list.mockReturnValue(of(page([], 2)));
    click('Reintentar');
    expect(friends.list).toHaveBeenLastCalledWith('', 2);
  });
  it('keeps a retry action after a rate-limit countdown finishes', async () => {
    friends.list.mockReturnValue(
      throwError(
        () =>
          new RateLimitError(
            new HttpErrorResponse({
              status: 429,
              headers: new HttpHeaders({ 'Retry-After': '2' }),
            }),
          ),
      ),
    );
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    await start();
    expect(
      [...root().querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Reintentar')!
        .disabled,
    ).toBe(true);
    await vi.advanceTimersByTimeAsync(2000);
    harness.detectChanges();
    friends.list.mockReturnValue(of(EMPTY_FRIENDS));
    click('Reintentar');
    expect(friends.list).toHaveBeenCalledTimes(2);
  });
  it('renders another user’s paginated friends without management actions', async () => {
    friends.forUser.mockReturnValue(of(page([FRIEND])));
    await start('/profile/otro/friends');
    expect(friends.forUser).toHaveBeenCalledWith('otro', 1);
    expect(root().querySelector('[role="tablist"]')).toBeNull();
    expect(root().querySelector('app-friendship-button')).toBeNull();
    expect(root().textContent).toContain('Amigos desde');
  });
  it('hides another user’s list when the server returns profile_restricted', async () => {
    friends.forUser.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 403, error: { code: 'profile_restricted' } }),
      ),
    );
    await start('/profile/otro/friends');
    expect(root().querySelector('[role="alert"]')!.textContent).toContain('privada');
    expect(root().querySelectorAll('app-friend-row')).toHaveLength(0);
  });
});
