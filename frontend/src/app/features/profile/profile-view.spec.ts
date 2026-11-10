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
import { DEFAULT_PRIVACY, MY_PROFILE, OTHER_PROFILE } from './profile-test-helpers';
import { ProfileEditResult } from './profile-edit';
import { UsersService } from './services/users';
import { ProfileDto } from './models/user';

describe('Social profile screen', () => {
  function setup(username?: string, profile: ProfileDto = OTHER_PROFILE) {
    const params = new BehaviorSubject(convertToParamMap(username ? { username } : {}));
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const users = {
      getMe: vi.fn().mockReturnValue(of(MY_PROFILE)),
      getPrivacy: vi.fn().mockReturnValue(of(DEFAULT_PRIVACY)),
      getProfile: vi.fn().mockReturnValue(of(profile)),
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
    vi.restoreAllMocks();
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

  it.each([0, 1, 12])(
    'shows the own friends count %i beside the name with a link to the own list',
    (count) => {
      const f = setup();
      f.users.getMe.mockReturnValue(of({ ...MY_PROFILE, friendsCount: count }));
      // Reload after replacing the initial mock response.
      f.fixture.componentInstance['load']();
      f.fixture.detectChanges();
      const identity = f.root.querySelector('[data-profile-identity]')!;
      expect(identity.querySelector('h2')?.textContent).toContain(MY_PROFILE.username);
      const link = identity.querySelector('[data-profile-friends]')!;
      expect(link.getAttribute('href')).toBe('/friends');
      expect(
        Array.from(link.querySelectorAll('span')).map((span) => span.textContent?.trim()),
      ).toEqual(['Amigos', String(count)]);
      expect(f.root.querySelectorAll('a[href="/friends"]')).toHaveLength(1);
    },
  );

  it('links the public count to that player’s friends and preserves the preview', () => {
    const f = setup('jugador', { ...OTHER_PROFILE, friendsCount: 12 });
    f.fixture.detectChanges();
    expect(
      f.root.querySelector('[data-profile-identity] [data-profile-friends]')?.getAttribute('href'),
    ).toBe('/profile/jugador/friends');
    expect(f.root.querySelector('app-friends-preview')).not.toBeNull();
  });

  it.each([
    ['private', 'Este perfil es privado'],
    ['friends_only', 'El contenido de este perfil es solo para amigos'],
  ] as const)(
    'hides every sensitive field for %s, even if present in the response',
    (restriction, message) => {
      const f = setup('jugador', {
        ...OTHER_PROFILE,
        isRestricted: true,
        restriction,
        bio: 'BIO SECRETA',
        friendsCount: 987,
      });
      f.fixture.detectChanges();
      expect(f.root.querySelector('h1')!.textContent).toContain('jugador');
      expect(f.root.querySelector('app-user-avatar')).not.toBeNull();
      expect(f.root.querySelector('[role="alert"]')!.textContent).toContain(message);
      expect(f.root.querySelector('app-profile-restriction svg')).not.toBeNull();
      expect(f.root.querySelector('app-friendship-button')).not.toBeNull();
      expect(f.root.textContent).not.toContain('BIO SECRETA');
      expect(f.root.textContent).not.toContain('987');
      expect(f.root.textContent).not.toContain('Se unió');
      expect(f.root.querySelector('app-friends-preview')).toBeNull();
      expect(f.root.querySelector('app-profile-privacy')).toBeNull();
      expect(f.root.querySelector('a[href="/profile/jugador/friends"]')).toBeNull();
    },
  );

  it('can send a request from a private profile without exposing its contents', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const profile: ProfileDto = { ...OTHER_PROFILE, isRestricted: true, restriction: 'private' };
    const f = setup('jugador', profile);
    f.fixture.detectChanges();
    f.users.getProfile.mockReturnValue(
      of({ ...profile, relationship: { status: 'request_sent', requestId: 7 } }),
    );
    [...f.root.querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'Agregar amigo')!
      .click();
    await vi.advanceTimersByTimeAsync(1);
    f.fixture.detectChanges();
    expect(TestBed.inject(FriendsService).send).toHaveBeenCalledWith('jugador');
    expect(f.users.getProfile).toHaveBeenCalledTimes(2);
    expect(f.root.textContent).toContain('Solicitud enviada');
    expect(f.root.textContent).toContain('Este perfil es privado');
    expect(f.root.querySelector('app-friends-preview')).toBeNull();
  });

  it('reveals only server-authorized content after acceptance and hides it after removal', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const restricted: ProfileDto = {
      ...OTHER_PROFILE,
      isRestricted: true,
      restriction: 'friends_only',
      relationship: { status: 'request_received', requestId: 7 },
    };
    const f = setup('jugador', restricted);
    f.fixture.detectChanges();
    f.users.getProfile.mockReturnValue(
      of({ ...OTHER_PROFILE, relationship: { status: 'friends' } }),
    );
    [...f.root.querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'Aceptar')!
      .click();
    await vi.advanceTimersByTimeAsync(1);
    f.fixture.detectChanges();
    expect(TestBed.inject(FriendsService).accept).toHaveBeenCalledWith(7);
    expect(f.root.textContent).toContain('Hola');
    expect(f.root.querySelector('app-friends-preview')).not.toBeNull();
    f.users.getProfile.mockReturnValue(of({ ...restricted, relationship: { status: 'none' } }));
    const friends = TestBed.inject(FriendsService) as unknown as ReturnType<
      typeof createFriendsMock
    >;
    friends.changes.next({ username: 'jugador', action: 'removed' });
    await vi.advanceTimersByTimeAsync(1);
    f.fixture.detectChanges();
    expect(f.root.textContent).not.toContain('Hola');
    expect(f.root.querySelector('app-friends-preview')).toBeNull();
    expect(f.root.textContent).toContain('El contenido de este perfil es solo para amigos');
  });

  it('rechecks access on focus and visible polling, and stops on destroy', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    const f = setup('jugador');
    f.fixture.detectChanges();
    f.users.getProfile.mockReturnValue(
      of({ ...OTHER_PROFILE, isRestricted: true, restriction: 'private' }),
    );
    window.dispatchEvent(new Event('focus'));
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(1);
    f.fixture.detectChanges();
    expect(f.users.getProfile).toHaveBeenCalledTimes(2);
    expect(f.root.textContent).toContain('Este perfil es privado');
    visibility.mockReturnValue('hidden');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(f.users.getProfile).toHaveBeenCalledTimes(2);
    visibility.mockReturnValue('visible');
    await vi.advanceTimersByTimeAsync(30_000);
    expect(f.users.getProfile).toHaveBeenCalledTimes(3);
    f.fixture.destroy();
    await vi.advanceTimersByTimeAsync(30_000);
    window.dispatchEvent(new Event('focus'));
    expect(f.users.getProfile).toHaveBeenCalledTimes(3);
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
    expect(f.root.querySelector('[data-profile-friends]')?.getAttribute('aria-label')).toBe(
      'Ver amigos de jugador: 9',
    );
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
