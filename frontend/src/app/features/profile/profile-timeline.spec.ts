import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of } from 'rxjs';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { LibraryService } from '../library/services/library';
import { ReviewService } from '../reviews/services/review';
import { TimelineService } from '../timeline/services/timeline';
import { ProfileComponent } from './profile';
import { MY_PROFILE } from './profile-test-helpers';
import { UsersService } from './services/users';

describe('Profile timeline tabs', () => {
  async function setup(url = '/profile') {
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const library = new Subject();
    const reviews = new Subject();
    const timeline = {
      getMine: vi.fn().mockReturnValue(of({ items: [], nextCursor: null, hasMore: false })),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'profile', component: ProfileComponent }]),
        provideHttpClient(),
        { provide: AuthService, useValue: auth },
        { provide: FriendsService, useValue: createFriendsMock() },
        { provide: UsersService, useValue: { getMe: () => of(MY_PROFILE) } },
        { provide: TimelineService, useValue: timeline },
        { provide: LibraryService, useValue: { changes$: library } },
        { provide: ReviewService, useValue: { changes$: reviews } },
      ],
    });
    const harness = await RouterTestingHarness.create(url);
    const root = harness.routeNativeElement!;
    return { harness, root, timeline, library, reviews, router: TestBed.inject(Router) };
  }
  it('leaves the existing profile as the default tab without fetching activity', async () => {
    const f = await setup();
    expect(f.root.querySelectorAll('[role="tab"]')).toHaveLength(2);
    expect(f.root.querySelector('[aria-selected="true"]')?.textContent?.trim()).toBe('Perfil');
    expect(f.root.querySelector<HTMLElement>('#profile-panel-profile')?.hidden).toBe(false);
    expect(f.timeline.getMine).not.toHaveBeenCalled();
  });
  it('restores the timeline from the URL and preserves unrelated query parameters', async () => {
    const f = await setup('/profile?tab=timeline&keep=yes');
    expect(f.root.querySelector('[aria-selected="true"]')?.textContent?.trim()).toBe(
      'Línea de tiempo',
    );
    expect(f.timeline.getMine).toHaveBeenCalledOnce();
    f.root.querySelector<HTMLButtonElement>('#profile-tab-profile')!.click();
    await f.harness.fixture.whenStable();
    f.harness.detectChanges();
    expect(f.router.url).toContain('tab=profile');
    expect(f.router.url).toContain('keep=yes');
    expect(f.root.querySelector<HTMLElement>('#profile-panel-timeline')!.hidden).toBe(true);
  });
  it('supports arrow keys, Home and End with roving tabindex, focus and URL updates', async () => {
    const f = await setup();
    for (const [key, id] of [
      ['ArrowRight', 'timeline'],
      ['ArrowRight', 'profile'],
      ['End', 'timeline'],
      ['Home', 'profile'],
      ['ArrowLeft', 'timeline'],
    ]) {
      const tab = f.root.querySelector<HTMLButtonElement>('[aria-selected="true"]')!;
      tab.focus();
      tab.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
      await f.harness.fixture.whenStable();
      f.harness.detectChanges();
      const active = f.root.querySelector<HTMLButtonElement>(`#profile-tab-${id}`)!;
      expect(active.getAttribute('aria-selected')).toBe('true');
      expect(active.tabIndex).toBe(0);
      expect(document.activeElement).toBe(active);
      expect(f.router.url).toContain(`tab=${id}`);
    }
  });
  it.each(['library', 'reviews'] as const)(
    'reloads the previously opened tab after %s changes',
    async (source) => {
      const f = await setup('/profile?tab=timeline');
      await f.router.navigateByUrl('/profile?tab=profile');
      f.harness.detectChanges();
      f[source].next({});
      expect(f.timeline.getMine).toHaveBeenCalledOnce();
      await f.router.navigateByUrl('/profile?tab=timeline');
      f.harness.detectChanges();
      expect(f.timeline.getMine).toHaveBeenCalledTimes(2);
      expect(f.timeline.getMine).toHaveBeenLastCalledWith({ limit: 20 });
    },
  );
});
