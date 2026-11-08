import { FriendsService } from '../friends/services/friends';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { Dialog } from '@angular/cdk/dialog';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { ProfileComponent } from './profile';
import { MY_PROFILE, OTHER_PROFILE } from './profile-test-helpers';
import { ProfileEditResult } from './profile-edit';
import { UsersService } from './services/users';

describe('Social profile screen', () => {
  function setup(username?: string) {
    const params = new BehaviorSubject(convertToParamMap(username ? { username } : {}));
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const users = {
      getMe: vi.fn().mockReturnValue(of(MY_PROFILE)),
      getProfile: vi.fn().mockReturnValue(of(OTHER_PROFILE)),
    };
    const closed = new Subject<ProfileEditResult>();
    const dialog = { open: vi.fn().mockReturnValue({ closed, close: vi.fn() }) };
    TestBed.configureTestingModule({
      providers: [
        { provide: FriendsService, useValue: createFriendsMock() },
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: UsersService, useValue: users },
        { provide: Dialog, useValue: dialog },
        {
          provide: ActivatedRoute,
          useValue: { paramMap: params, snapshot: { paramMap: params.value } },
        },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(ProfileComponent);
    return { fixture, root: fixture.nativeElement as HTMLElement, users, navigate, closed, dialog };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('loads own profile, formats join date, invites a bio and focuses the heading', () => {
    const f = setup();
    f.fixture.detectChanges();
    expect(f.users.getMe).toHaveBeenCalledOnce();
    expect(f.root.textContent).toContain('Se unió en marzo de 2026');
    expect(f.root.textContent).toContain('Agregá una biografía');
    expect(f.root.querySelector('[data-action="edit-profile"]')).not.toBeNull();
    expect(document.activeElement).toBe(f.root.querySelector('h1'));
  });
  it('loads another profile with literal multiline text and no edit action', () => {
    const f = setup('jugador');
    f.users.getProfile.mockReturnValue(
      of({ ...OTHER_PROFILE, bio: '<img src=x onerror=alert(1)>\nHola' }),
    );
    // The route emits in the constructor; emit a new response through a retry.
    f.fixture.destroy();
    const fixture = TestBed.createComponent(ProfileComponent);
    fixture.detectChanges();
    expect(f.users.getProfile).toHaveBeenCalledWith('jugador');
    expect(fixture.nativeElement.textContent).toContain('<img src=x onerror=alert(1)>\nHola');
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-action="edit-profile"]')).toBeNull();
  });
  it('redirects a case-insensitive own URL without requesting the public profile', () => {
    const f = setup('ANA');
    f.fixture.detectChanges();
    expect(f.navigate).toHaveBeenCalledWith(['/profile'], { replaceUrl: true });
    expect(f.users.getProfile).not.toHaveBeenCalled();
  });

  it('shows friendship controls and refreshes the public count after social changes', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const f = setup('jugador');
    f.fixture.detectChanges();
    expect(f.root.querySelector('app-friendship-button')).not.toBeNull();
    expect(f.root.querySelector('app-friends-preview')).not.toBeNull();
    f.users.getProfile.mockReturnValue(
      of({ ...OTHER_PROFILE, friendsCount: 9, relationship: { status: 'friends' } }),
    );
    const service = TestBed.inject(FriendsService) as unknown as ReturnType<
      typeof createFriendsMock
    >;
    service.changes.next({ username: 'jugador', action: 'accepted' });
    await vi.advanceTimersByTimeAsync(1);
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain('9 amigos');
    expect(f.root.querySelector('a[href="/profile/jugador/friends"]')).not.toBeNull();
  });
  it('announces saved edits and navigates to the canonical route after a rename', () => {
    const f = setup();
    f.fixture.detectChanges();
    const button = f.root.querySelector<HTMLButtonElement>('[data-action="edit-profile"]')!;
    button.click();
    expect(f.dialog.open.mock.calls[0][1]).toEqual(
      expect.objectContaining({ restoreFocus: button, ariaModal: true }),
    );
    f.closed.next({
      profile: { ...MY_PROFILE, username: 'nuevo' },
      renamed: true,
      warning: 'Aviso',
    });
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain('Perfil actualizado');
    expect(f.root.textContent).toContain('nuevo');
    expect(f.root.textContent).toContain('Aviso');
    expect(f.navigate).toHaveBeenCalledWith(['/profile'], { replaceUrl: true });
  });
  it.each(['missing', 'network', 'rate', 'loading'])(
    'renders the %s state and retry behavior',
    (state) => {
      if (state === 'rate') vi.useFakeTimers();
      const f = setup();
      f.fixture.destroy();
      if (state === 'loading') f.users.getMe.mockReturnValue(new Subject());
      else
        f.users.getMe.mockReturnValue(
          throwError(() =>
            state === 'missing'
              ? new HttpErrorResponse({ status: 404, error: { code: 'user_not_found' } })
              : state === 'rate'
                ? new RateLimitError(
                    new HttpErrorResponse({
                      status: 429,
                      headers: new HttpHeaders({ 'Retry-After': '2' }),
                    }),
                  )
                : new HttpErrorResponse({ status: 0 }),
          ),
        );
      const fixture = TestBed.createComponent(ProfileComponent);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      if (state === 'loading') expect(root.querySelector('[data-profile-skeleton]')).not.toBeNull();
      else if (state === 'missing') {
        expect(root.querySelector('[role="alert"]')!.textContent).toContain(
          'No encontramos a este jugador',
        );
        expect(root.querySelector('a')!.getAttribute('href')).toBe('/games');
      } else {
        const retry = root.querySelector('button')!;
        expect(root.querySelector('[role="alert"]')).not.toBeNull();
        if (state === 'rate') {
          expect(retry.disabled).toBe(true);
          vi.advanceTimersByTime(2000);
          fixture.detectChanges();
        }
        f.users.getMe.mockReturnValue(of(MY_PROFILE));
        retry.click();
        fixture.detectChanges();
        expect(root.textContent).toContain('ana');
      }
    },
  );
});
