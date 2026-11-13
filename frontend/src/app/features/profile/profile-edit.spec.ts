import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { By } from '@angular/platform-browser';
import { AvatarEditorComponent } from './avatar-editor';
import { TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createAuthMock } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { ProfileEditComponent } from './profile-edit';
import { MY_PROFILE } from './profile-test-helpers';
import { UsersService } from './services/users';

describe('Profile editor', () => {
  function setup(availableAt: string | null = null) {
    const users = { updateMe: vi.fn().mockReturnValue(of(MY_PROFILE)) };
    const auth = {
      ...createAuthMock(),
      refresh: vi.fn().mockReturnValue(of('new-token')),
      updateCurrentUsername: vi.fn(),
    };
    const dialog = { close: vi.fn(), disableClose: false };
    TestBed.configureTestingModule({
      providers: [
        { provide: UsersService, useValue: users },
        { provide: AuthService, useValue: auth },
        { provide: DialogRef, useValue: dialog },
        {
          provide: DIALOG_DATA,
          useValue: { ...MY_PROFILE, usernameChangeAvailableAt: availableAt },
        },
      ],
    });
    const fixture = TestBed.createComponent(ProfileEditComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const save = () => {
      root
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
    };
    const input = (id: string, value: string) => {
      const field = root.querySelector<HTMLInputElement>(`#${id}`)!;
      field.value = value;
      field.dispatchEvent(new Event('input'));
      fixture.detectChanges();
    };
    const submit = () => root.querySelector<HTMLButtonElement>('[type="submit"]')!;
    return { fixture, root, users, auth, dialog, input, save, submit };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('synchronizes a saved photo with the session and blocks closing during an avatar upload', () => {
    const f = setup();
    const avatar = f.fixture.debugElement.query(By.directive(AvatarEditorComponent))
      .componentInstance as AvatarEditorComponent;
    avatar.busyChanged.emit(true);
    f.fixture.detectChanges();
    expect(f.dialog.disableClose).toBe(true);
    expect(f.root.querySelector<HTMLButtonElement>('form button[type="button"]')!.disabled).toBe(
      true,
    );
    avatar.changed.emit('/api/avatars/photo.webp');
    expect(f.auth.updateCurrentAvatar).toHaveBeenCalledWith(
      MY_PROFILE.id,
      '/api/avatars/photo.webp',
    );
    avatar.busyChanged.emit(false);
    f.fixture.detectChanges();
    expect(f.dialog.disableClose).toBe(false);
    expect(f.root.querySelector<HTMLButtonElement>('form button[type="button"]')!.disabled).toBe(
      false,
    );
  });

  it('disables unchanged and invalid values and counts Unicode scalars after normalization', () => {
    const f = setup();
    expect(f.submit().disabled).toBe(true);
    f.input('profile-username', 'ANA');
    expect(f.submit().disabled).toBe(true);
    f.input('profile-username', 'a.b');
    expect(f.submit().disabled).toBe(true);
    f.input('profile-username', 'ana');
    f.input('profile-bio', '🎮'.repeat(300));
    expect(f.root.querySelector('#bio-counter')!.textContent).toContain('300 / 300');
    expect(f.submit().disabled).toBe(false);
    f.input('profile-bio', '🎮'.repeat(301));
    expect(f.submit().disabled).toBe(true);
    f.save();
    expect(f.users.updateMe).not.toHaveBeenCalled();
  });
  it('locks username until the server date while allowing bio edits', () => {
    const f = setup('2099-12-25T12:00:00Z');
    expect(f.root.querySelector<HTMLInputElement>('#profile-username')!.disabled).toBe(true);
    expect(f.root.textContent).toContain('Podrás cambiarlo el 25/12/2099');
    f.input('profile-bio', ' Hola\r\nMundo ');
    f.save();
    expect(f.users.updateMe).toHaveBeenCalledWith({ bio: 'Hola\nMundo' });
    expect(f.auth.refresh).not.toHaveBeenCalled();
    expect(f.dialog.close).toHaveBeenCalledWith({
      profile: MY_PROFILE,
      warning: null,
      renamed: false,
    });
  });
  it.each([
    ['username_taken', 'Ese nombre ya está en uso'],
    ['username_reserved', 'Ese nombre está reservado'],
    ['invalid_username', 'Usá entre 3 y 20'],
    ['invalid_bio', 'La biografía debe tener hasta 300'],
    ['username_change_too_soon', 'Todavía no podés'],
  ])('preserves input on %s and allows correction', (code, message) => {
    const f = setup();
    f.users.updateMe.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { extensions: { code, availableAt: '2099-12-25T12:00:00Z' } },
          }),
      ),
    );
    f.input('profile-username', 'nuevo');
    f.input('profile-bio', 'Mi bio');
    f.save();
    expect(f.root.querySelector('[role="alert"]')!.textContent).toContain(message);
    expect(f.root.querySelector<HTMLInputElement>('#profile-username')!.value).toBe('nuevo');
    expect(f.root.querySelector<HTMLTextAreaElement>('#profile-bio')!.value).toBe('Mi bio');
    expect(f.dialog.close).not.toHaveBeenCalled();
    if (code === 'username_change_too_soon') expect(f.root.textContent).toContain('25/12/2099');
  });
  it('handles top-level ProblemDetails metadata', () => {
    const f = setup();
    f.users.updateMe.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { code: 'username_change_too_soon', availableAt: '2099-12-25T12:00:00Z' },
          }),
      ),
    );
    f.input('profile-username', 'nuevo');
    f.save();
    expect(f.root.textContent).toContain('25/12/2099');
    expect(f.root.querySelector<HTMLInputElement>('#profile-username')!.disabled).toBe(true);
  });
  it('shows network errors without losing text', () => {
    const f = setup();
    f.users.updateMe.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.input('profile-bio', 'Mi bio');
    f.save();
    expect(f.root.querySelector('[role="alert"]')!.textContent).toContain('Revisá tu conexión');
    expect(f.submit().disabled).toBe(false);
  });
  it('counts down 429 and allows retry without automatic submission', () => {
    vi.useFakeTimers();
    const f = setup();
    f.users.updateMe.mockReturnValue(
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
    f.input('profile-bio', 'Mi bio');
    f.save();
    expect(f.submit().disabled).toBe(true);
    expect(f.root.querySelector('[role="alert"]')!.textContent).toContain('2');
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    expect(f.submit().disabled).toBe(false);
    expect(f.users.updateMe).toHaveBeenCalledTimes(1);
  });
  it('prevents duplicate saves and refreshes after a renamed profile is saved', () => {
    const f = setup();
    const response = new Subject<typeof MY_PROFILE>();
    f.users.updateMe.mockReturnValue(response);
    f.input('profile-username', 'nuevo');
    f.save();
    f.save();
    expect(f.users.updateMe).toHaveBeenCalledTimes(1);
    expect(f.dialog.disableClose).toBe(true);
    response.next({ ...MY_PROFILE, username: 'nuevo' });
    response.complete();
    expect(f.auth.updateCurrentUsername).toHaveBeenCalledWith(1, 'nuevo');
    expect(f.auth.refresh).toHaveBeenCalledWith({ redirectOnFailure: false });
    expect(f.dialog.close).toHaveBeenCalledWith({
      profile: { ...MY_PROFILE, username: 'nuevo' },
      warning: null,
      renamed: true,
    });
  });
  it('keeps a saved rename and session when refresh fails over the network', () => {
    const f = setup();
    f.users.updateMe.mockReturnValue(of({ ...MY_PROFILE, username: 'nuevo' }));
    f.auth.refresh.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.input('profile-username', 'nuevo');
    f.save();
    expect(f.dialog.close).toHaveBeenCalledWith(
      expect.objectContaining({
        renamed: true,
        warning: expect.stringContaining('Tu perfil se guardó'),
      }),
    );
    expect(f.auth.clearSession).not.toHaveBeenCalled();
    expect(f.auth.logout).not.toHaveBeenCalled();
  });
});
