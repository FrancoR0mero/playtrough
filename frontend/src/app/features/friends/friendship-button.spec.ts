import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { OTHER_PROFILE } from '../profile/profile-test-helpers';
import { UsersService } from '../profile/services/users';
import { createFriendsMock } from './friends-test-helpers';
import { FriendshipButtonComponent } from './friendship-button';
import { RelationshipDto } from './models/friend';
import { FriendsService } from './services/friends';

describe('Friendship button', () => {
  function setup(status: RelationshipDto['status'] = 'none', username = 'bea') {
    const friends = createFriendsMock();
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const users = { getProfile: vi.fn().mockReturnValue(of(OTHER_PROFILE)) };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: FriendsService, useValue: friends },
        { provide: UsersService, useValue: users },
      ],
    });
    const fixture = TestBed.createComponent(FriendshipButtonComponent);
    fixture.componentRef.setInput('username', username);
    fixture.componentRef.setInput('relationship', { status, requestId: 7 });
    const changed = vi.fn();
    fixture.componentInstance.relationshipChanged.subscribe(changed);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const click = (text: string) => {
      [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text)!.click();
      fixture.detectChanges();
    };
    return { fixture, root, friends, users, changed, click };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('hides actions for the current account regardless of case', () => {
    const f = setup('none', 'ANA');
    expect(f.root.querySelector('button')).toBeNull();
  });
  it.each([
    ['none', 'Agregar amigo', 'send', { status: 'request_sent', requestId: 7 }],
    ['request_sent', 'Cancelar', 'cancel', { status: 'none' }],
    ['request_received', 'Aceptar', 'accept', { status: 'friends' }],
    ['request_received', 'Rechazar', 'reject', { status: 'none' }],
  ] as const)('%s offers %s and emits the resulting state', (status, label, method, expected) => {
    const f = setup(status);
    f.click(label);
    expect(f.friends[method]).toHaveBeenCalledOnce();
    expect(f.changed).toHaveBeenCalledWith(expected);
    expect(f.root.querySelector('[role="status"]')?.textContent?.trim()).not.toBe('');
    if (method === 'send') expect(f.friends.send).toHaveBeenCalledWith('bea');
    else if (method === 'accept') expect(f.friends.accept).toHaveBeenCalledWith(7);
    else expect(f.friends[method]).toHaveBeenCalledWith(7, 'bea');
  });
  it('requires inline confirmation and does not remove when cancelled', () => {
    const f = setup('friends');
    f.click('Eliminar amigo');
    expect(f.root.textContent).toContain('¿Eliminar tu amistad con bea?');
    f.click('Conservar amistad');
    expect(f.friends.remove).not.toHaveBeenCalled();
    f.click('Eliminar amigo');
    f.click('Sí, eliminar');
    expect(f.friends.remove).toHaveBeenCalledWith('bea');
    expect(f.changed).toHaveBeenCalledWith({ status: 'none' });
  });
  it('blocks duplicate mutations while processing', () => {
    const f = setup();
    const result = new Subject();
    f.friends.send.mockReturnValue(result);
    f.click('Agregar amigo');
    f.click('Agregar amigo');
    expect(f.friends.send).toHaveBeenCalledOnce();
    expect(f.root.textContent).toContain('Procesando');
  });
  it.each(['already_friends', 'request_already_sent', 'request_not_pending', 'request_not_found'])(
    'reconciles %s with the real relationship',
    (code) => {
      const f = setup();
      f.users.getProfile.mockReturnValue(
        of({ ...OTHER_PROFILE, relationship: { status: 'friends' } }),
      );
      f.friends.send.mockReturnValue(
        throwError(() => new HttpErrorResponse({ status: 409, error: { code } })),
      );
      f.click('Agregar amigo');
      expect(f.users.getProfile).toHaveBeenCalledWith('bea');
      expect(f.changed).toHaveBeenCalledWith({ status: 'friends' });
      expect(f.root.querySelector('[role="alert"]')).not.toBeNull();
    },
  );
  it.each([true, false])('uses request_already_received metadata (nested=%s)', (nested) => {
    const f = setup();
    const problem = { code: 'request_already_received', requestId: 81 };
    f.friends.send.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 409, error: nested ? { extensions: problem } : problem }),
      ),
    );
    f.click('Agregar amigo');
    expect(f.changed).toHaveBeenCalledWith({ status: 'request_received', requestId: 81 });
    f.click('Aceptar');
    expect(f.friends.accept).toHaveBeenCalledWith(81);
  });
  it.each([
    ['cooldown_active', 'Podrás enviar otra solicitud el 25/12/2099'],
    ['too_many_pending_requests', 'demasiadas solicitudes'],
    ['user_not_found', 'No encontramos a este jugador'],
    ['cannot_befriend_self', 'No podés agregarte'],
    ['unknown', 'Revisá tu conexión'],
  ])('shows %s in an alert without claiming success', (code, message) => {
    const f = setup();
    f.friends.send.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { extensions: { code, availableAt: '2099-12-25T12:00:00Z' } },
          }),
      ),
    );
    f.click('Agregar amigo');
    expect(f.root.querySelector('[role="alert"]')!.textContent).toContain(message);
    expect(f.changed).not.toHaveBeenCalled();
  });
  it('counts down rate limits before allowing an explicit retry', () => {
    vi.useFakeTimers();
    const f = setup();
    f.friends.send.mockReturnValue(
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
    f.click('Agregar amigo');
    expect(f.root.querySelector('button')!.disabled).toBe(true);
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    expect(f.root.querySelector('button')!.disabled).toBe(false);
    expect(f.friends.send).toHaveBeenCalledOnce();
  });
  it('offers reconciliation retry if the follow-up profile request fails', () => {
    const f = setup();
    f.friends.send.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: { code: 'already_friends' } })),
    );
    f.users.getProfile.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.click('Agregar amigo');
    expect(f.root.textContent).toContain('Actualizar estado');
    f.users.getProfile.mockReturnValue(of(OTHER_PROFILE));
    f.click('Actualizar estado');
    expect(f.changed).toHaveBeenCalledWith({ status: 'none' });
  });
});
