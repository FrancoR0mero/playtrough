import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { PrivacyDto } from './models/user';
import { ProfilePrivacyComponent } from './profile-privacy';
import { DEFAULT_PRIVACY } from './profile-test-helpers';
import { UsersService } from './services/users';

describe('Privacy preferences', () => {
  function setup(deferred = false) {
    const pending = new Subject<PrivacyDto>();
    const users = {
      getPrivacy: vi.fn().mockReturnValue(deferred ? pending : of(DEFAULT_PRIVACY)),
      updatePrivacy: vi.fn().mockImplementation((value: PrivacyDto) => of(value)),
    };
    TestBed.configureTestingModule({ providers: [{ provide: UsersService, useValue: users }] });
    const fixture = TestBed.createComponent(ProfilePrivacyComponent);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const select = (key: keyof PrivacyDto, value: string) => {
      root.querySelector<HTMLInputElement>(`input[name="${key}"][value="${value}"]`)!.click();
      fixture.detectChanges();
    };
    const submit = () => root.querySelector<HTMLButtonElement>('button[type="submit"]')!;
    const save = () => {
      root
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      fixture.detectChanges();
    };
    return { fixture, root, users, pending, select, submit, save };
  }
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('loads current preferences, shows a skeleton and disables unchanged values', () => {
    const f = setup(true);
    expect(f.root.querySelector('[aria-label="Cargando privacidad"]')).not.toBeNull();
    f.pending.next({ profileVisibility: 'friends', historyVisibility: 'private' });
    f.pending.complete();
    f.fixture.detectChanges();
    expect(
      f.root.querySelector<HTMLInputElement>('input[name="profileVisibility"][value="friends"]')!
        .checked,
    ).toBe(true);
    expect(
      f.root.querySelector<HTMLInputElement>('input[name="historyVisibility"][value="private"]')!
        .checked,
    ).toBe(true);
    expect(f.submit().disabled).toBe(true);
    f.save();
    expect(f.users.updatePrivacy).not.toHaveBeenCalled();
    expect(f.root.textContent).toContain(
      'por ahora no cambia quién puede ver tu biblioteca ni tus reseñas',
    );
  });
  it('sends both changed preferences and announces success', () => {
    const f = setup();
    f.select('profileVisibility', 'friends');
    f.select('historyVisibility', 'private');
    expect(f.submit().disabled).toBe(false);
    f.save();
    expect(f.users.updatePrivacy).toHaveBeenCalledWith({
      profileVisibility: 'friends',
      historyVisibility: 'private',
    });
    expect(f.root.querySelector('[role="status"]')!.textContent).toContain(
      'Privacidad actualizada',
    );
    expect(f.submit().disabled).toBe(true);
    f.select('profileVisibility', 'public');
    f.select('profileVisibility', 'friends');
    expect(f.submit().disabled).toBe(true);
  });
  it('supports arrows, wrapping, Home and End within each labeled radio group', () => {
    const f = setup();
    const groups = f.root.querySelectorAll('[role="radiogroup"]');
    for (const group of groups) {
      expect(group.getAttribute('aria-labelledby')).toBeTruthy();
      const radios = group.querySelectorAll<HTMLInputElement>('input');
      radios[0].focus();
      for (const [key, index] of [
        ['ArrowRight', 1],
        ['ArrowDown', 2],
        ['ArrowRight', 0],
        ['ArrowLeft', 2],
        ['Home', 0],
        ['End', 2],
      ] as const) {
        group.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
        f.fixture.detectChanges();
        expect(radios[index].checked).toBe(true);
        expect(document.activeElement).toBe(radios[index]);
      }
    }
  });
  it.each(['invalid_visibility', 'network'])(
    'preserves selections on %s without announcing success',
    (code) => {
      const f = setup();
      f.users.updatePrivacy.mockReturnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: code === 'network' ? 0 : 400,
              error: { extensions: { code } },
            }),
        ),
      );
      f.select('profileVisibility', 'private');
      f.save();
      expect(f.root.querySelector('[role="alert"]')!.textContent).toContain(
        code === 'network' ? 'Revisá tu conexión' : 'Elegí una opción válida',
      );
      expect(f.root.querySelector<HTMLInputElement>('input[value="private"]')!.checked).toBe(true);
      expect(f.submit().disabled).toBe(false);
      expect(f.root.textContent).not.toContain('Privacidad actualizada');
    },
  );
  it('disables controls and duplicate submissions while saving', () => {
    const f = setup();
    const pending = new Subject<PrivacyDto>();
    f.users.updatePrivacy.mockReturnValue(pending);
    f.select('profileVisibility', 'private');
    f.save();
    f.save();
    expect(f.users.updatePrivacy).toHaveBeenCalledOnce();
    expect(f.submit().disabled).toBe(true);
    expect(f.root.querySelector('fieldset')!.disabled).toBe(true);
  });
  it('allows explicit retry after rate limiting while keeping choices', () => {
    vi.useFakeTimers();
    const f = setup();
    f.users.updatePrivacy.mockReturnValue(
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
    f.select('historyVisibility', 'friends');
    f.save();
    expect(f.submit().disabled).toBe(true);
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    expect(f.submit().disabled).toBe(false);
    expect(f.users.updatePrivacy).toHaveBeenCalledOnce();
  });
  it('retries a failed initial load before exposing editable values', () => {
    const f = setup(true);
    f.pending.error(new HttpErrorResponse({ status: 0 }));
    f.fixture.detectChanges();
    expect(f.root.querySelector('form')).toBeNull();
    expect(f.root.querySelector('[role="alert"]')).not.toBeNull();
    f.users.getPrivacy.mockReturnValue(of(DEFAULT_PRIVACY));
    f.root.querySelector('button')!.click();
    f.fixture.detectChanges();
    expect(f.root.querySelector('form')).not.toBeNull();
    expect(f.users.getPrivacy).toHaveBeenCalledTimes(2);
  });
});
