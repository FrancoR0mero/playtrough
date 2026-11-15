import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { TimelineService } from '../timeline/services/timeline';
import { timelineEntry } from '../timeline/timeline-test-helpers';
import { ProfileComponent } from './profile';
import { OTHER_PROFILE } from './profile-test-helpers';
import { UsersService } from './services/users';

describe('Other profile timeline integration', () => {
  afterEach(() => vi.useRealTimers());
  async function setup(restricted = false, timelineRestricted = false) {
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const friends = createFriendsMock();
    const profile = {
      ...OTHER_PROFILE,
      username: 'bea',
      isRestricted: restricted,
      restriction: restricted ? ('private' as const) : null,
    };
    const users = { getProfile: vi.fn().mockReturnValue(of(profile)) };
    const timeline = {
      getUserPeriods: vi.fn().mockReturnValue(of({ years: [] })),
      getMine: vi.fn(),
      getUser: vi
        .fn()
        .mockReturnValue(of({ items: [timelineEntry()], nextCursor: null, hasMore: false })),
    };
    if (timelineRestricted)
      timeline.getUser.mockReturnValueOnce(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 403,
              error: { code: 'history_restricted', restriction: 'private' },
            }),
        ),
      );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'profile/:username', component: ProfileComponent }]),
        provideHttpClient(),
        { provide: AuthService, useValue: auth },
        { provide: FriendsService, useValue: friends },
        { provide: UsersService, useValue: users },
        { provide: TimelineService, useValue: timeline },
      ],
    });
    const harness = await RouterTestingHarness.create('/profile/bea?tab=timeline');
    return {
      harness,
      root: harness.routeNativeElement!,
      users,
      friends,
      timeline,
      profile,
      router: TestBed.inject(Router),
    };
  }
  it('opens the visible profile timeline with the correct username and URL tab', async () => {
    const f = await setup();
    expect(f.root.querySelector('h1')?.textContent).toContain('Perfil de bea');
    expect(f.root.querySelectorAll('[role="tab"]')).toHaveLength(2);
    expect(f.root.querySelector('[aria-selected="true"]')?.textContent).toContain(
      'Línea de tiempo',
    );
    expect(f.timeline.getUser).toHaveBeenCalledExactlyOnceWith('bea', { limit: 20 });
    expect(f.timeline.getMine).not.toHaveBeenCalled();
    expect(f.root.querySelector('app-timeline')?.textContent).toContain('empezó a jugar a');
    f.root.querySelector<HTMLButtonElement>('#profile-tab-profile')!.click();
    await f.harness.fixture.whenStable();
    f.harness.detectChanges();
    expect(f.router.url).toBe('/profile/bea?tab=profile');
    expect(f.root.querySelector<HTMLElement>('#profile-panel-profile')!.hidden).toBe(false);
  });
  it('does not show tabs or request timeline when the whole profile is restricted', async () => {
    const f = await setup(true);
    expect(f.root.querySelector('[role="tablist"], app-timeline')).toBeNull();
    expect(f.timeline.getUser).not.toHaveBeenCalled();
    expect(f.root.textContent).toContain('Este perfil es privado');
  });
  it('shows an independently restricted timeline using the existing friendship button', async () => {
    const f = await setup(false, true);
    expect(f.root.querySelectorAll('[role="tab"]')).toHaveLength(2);
    const timeline = f.root.querySelector('app-timeline')!;
    expect(timeline.textContent).toContain('El historial de este jugador es privado');
    expect(timeline.querySelector('app-friendship-button')).not.toBeNull();
    expect(timeline.querySelector('ol')).toBeNull();
  });
  it('rechecks access when friendship changes and removes tabs if the profile becomes restricted', async () => {
    const f = await setup();
    vi.useFakeTimers();
    f.users.getProfile.mockReturnValue(
      of({ ...f.profile, isRestricted: true, restriction: 'private' }),
    );
    f.friends.changes.next({ username: 'bea', action: 'removed' });
    f.harness.detectChanges();
    expect(f.root.querySelector('app-timeline ol')).toBeNull();
    await vi.advanceTimersByTimeAsync(0);
    f.harness.detectChanges();
    expect(f.root.querySelector('[role="tablist"], app-timeline')).toBeNull();
    expect(f.root.textContent).toContain('Este perfil es privado');
  });
  it('unlocks the tab after accepting an incoming friendship from a restricted profile', async () => {
    const f = await setup(true);
    vi.useFakeTimers();
    f.users.getProfile.mockReturnValue(
      of({
        ...f.profile,
        isRestricted: false,
        restriction: null,
        relationship: { status: 'friends' },
      }),
    );
    f.friends.changes.next({ username: 'bea', action: 'accepted' });
    await vi.advanceTimersByTimeAsync(0);
    f.harness.detectChanges();
    expect(f.root.querySelectorAll('[role="tab"]')).toHaveLength(2);
    expect(f.timeline.getUser).toHaveBeenCalledExactlyOnceWith('bea', { limit: 20 });
  });
  it('cancels the previous user request on navigation and clears old activity', async () => {
    const f = await setup();
    f.users.getProfile.mockReturnValue(of({ ...f.profile, username: 'carla' }));
    await f.router.navigateByUrl('/profile/carla?tab=timeline');
    f.harness.detectChanges();
    expect(f.timeline.getUser).toHaveBeenLastCalledWith('carla', { limit: 20 });
    expect(f.root.querySelector('h1')?.textContent).toContain('Perfil de carla');
  });
});
