import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { createAuthMock, createTokenStorageMock } from './auth-test-helpers';
import type { AuthMessage } from './models/password-recovery';
import { ResetPasswordComponent } from './reset-password';
import { AuthService } from './services/auth';
import { TokenStorageService } from './services/token-storage';

describe('ResetPasswordComponent', () => {
  let fixture: ComponentFixture<ResetPasswordComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let response: Subject<AuthMessage>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let navigateByUrl: ReturnType<typeof vi.spyOn>;
  let route: { snapshot: { queryParamMap: ReturnType<typeof convertToParamMap> } };

  beforeEach(() => {
    localStorage.clear();
    auth = createAuthMock();
    response = new Subject<AuthMessage>();
    auth.resetPassword.mockReturnValue(response);
  });
  afterEach(() => {
    fixture?.destroy();
    response.complete();
    localStorage.clear();
    vi.restoreAllMocks();
  });

  function render(
    params: Record<string, string | undefined> = {
      email: 'ana@example.com',
      token: 'url-safe-token',
    },
  ): void {
    route = { snapshot: { queryParamMap: convertToParamMap(params) } };
    TestBed.configureTestingModule({
      imports: [ResetPasswordComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: createTokenStorageMock() },
        { provide: ActivatedRoute, useValue: route },
      ],
    });
    const router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    navigateByUrl = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
  }
  const button = () =>
    fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
  function input(field: string, value: string, touch = true): HTMLInputElement {
    const element = fixture.nativeElement.querySelector(
      `[formControlName="${field}"]`,
    ) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event('input'));
    if (touch) element.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    return element;
  }
  function validForm(): void {
    input('newPassword', 'NuevaClave123');
    input('confirmPassword', 'NuevaClave123');
  }
  function submit(): void {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }

  it.each([{}, { email: 'ana@example.com' }, { token: 'token' }, { email: ' ', token: 'token' }])(
    'MissingLinkParameters_ShowsInvalidStateAndRecoveryLink: %j',
    (params) => {
      render(params);
      expect(fixture.nativeElement.querySelector('form')).toBeNull();
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'inválido o venció',
      );
      expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(
        '/auth/forgot-password',
      );
      expect(auth.resetPassword).not.toHaveBeenCalled();
    },
  );

  it('ReadLink_ReplacesHistoryEntryWithoutQueryParams', () => {
    render();
    expect(navigate).toHaveBeenCalledWith([], {
      relativeTo: route,
      queryParams: {},
      replaceUrl: true,
    });
    expect(auth.clearSession).not.toHaveBeenCalled();
  });

  it('UntouchedForm_HidesErrorsAndDisablesSubmit', () => {
    render();
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    input('newPassword', 'x', false);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it.each([
    ['', 'obligatoria'],
    ['Short1', '8 caracteres'],
    ['lowercase123', 'mayúscula'],
    ['UPPERCASE123', 'minúscula'],
    ['NoDigitsHere', 'número'],
  ])('PasswordPolicy_RejectsInvalidPassword: %s', (value, message) => {
    render();
    input('newPassword', value);
    input('confirmPassword', value);
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('#new-password-error').getAttribute('role')).toBe(
      'alert',
    );
    expect(fixture.nativeElement.querySelector('#new-password-error').textContent).toContain(
      message,
    );
  });

  it('MismatchedPasswords_ShowsAccessibleErrorAndFocusesConfirmation', () => {
    render();
    input('newPassword', 'NuevaClave123');
    const confirm = input('confirmPassword', 'OtraClave123');
    const focus = vi.spyOn(confirm, 'focus');
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('#confirm-password-error').textContent).toContain(
      'no coinciden',
    );
    submit();
    expect(focus).toHaveBeenCalled();
    expect(auth.resetPassword).not.toHaveBeenCalled();
  });

  it('ValidSubmission_UsesOriginalLinkValuesAndShowsLoadingWithoutDuplicateRequests', () => {
    render();
    validForm();
    submit();
    expect(auth.resetPassword).toHaveBeenCalledWith(
      'ana@example.com',
      'url-safe-token',
      'NuevaClave123',
      'NuevaClave123',
    );
    expect(button().disabled).toBe(true);
    expect(button().textContent).toContain('Guardando');
    submit();
    expect(auth.resetPassword).toHaveBeenCalledTimes(1);
  });

  it.each([true, false])(
    'SuccessfulReset_ClearsSessionBeforeRedirectingToLogin: authenticated=%s',
    (active) => {
      render();
      auth.isAuthenticated.set(active);
      validForm();
      submit();
      navigateByUrl.mockImplementation(() => {
        expect(auth.clearSession).toHaveBeenCalledOnce();
        return Promise.resolve(true);
      });
      response.next({ message: 'ok' });
      response.complete();
      expect(navigateByUrl).toHaveBeenCalledWith('/auth/login?passwordUpdated=true', {
        replaceUrl: true,
      });
      expect((fixture.nativeElement.querySelector('#new-password') as HTMLInputElement).value).toBe(
        '',
      );
    },
  );

  it('InvalidToken_ShowsRecoveryActionInsteadOfForm', () => {
    render();
    validForm();
    submit();
    response.error(
      new HttpErrorResponse({
        status: 400,
        error: {
          title: 'Invalid password reset request',
          detail: 'The password reset link is invalid or has expired.',
        },
      }),
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'inválido o venció',
    );
    expect(fixture.nativeElement.querySelector('a').textContent).toContain('Pedir un nuevo link');
  });

  it('WeakPassword_ShowsServerRuleUnderPasswordAndAllowsCorrection', () => {
    render();
    validForm();
    submit();
    const focus = vi.spyOn(fixture.nativeElement.querySelector('#new-password'), 'focus');
    response.error(
      new HttpErrorResponse({
        status: 400,
        error: { errors: { NewPassword: ['Regla del servidor.'] } },
      }),
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#new-password-error').textContent).toContain(
      'Regla del servidor.',
    );
    expect(focus).toHaveBeenCalled();
    expect(auth.clearSession).not.toHaveBeenCalled();
    input('newPassword', 'OtraClave456');
    input('confirmPassword', 'OtraClave456');
    expect(fixture.nativeElement.querySelector('#new-password-error')).toBeNull();
    response = new Subject<AuthMessage>();
    auth.resetPassword.mockReturnValue(response);
    submit();
    expect(auth.resetPassword).toHaveBeenCalledTimes(2);
  });

  it.each([0, 500])('FailedRequest_%s_ShowsGenericErrorAndAllowsRetry', (status) => {
    render();
    validForm();
    submit();
    response.error(new HttpErrorResponse({ status }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'No pudimos cambiar la contraseña',
    );
    expect(button().disabled).toBe(false);
    response = new Subject<AuthMessage>();
    auth.resetPassword.mockReturnValue(response);
    submit();
    expect(auth.resetPassword).toHaveBeenCalledTimes(2);
  });

  it('VisibilityToggle_ChangesBothFieldsAndKeepsNewPasswordAutocomplete', () => {
    render();
    validForm();
    const toggle = fixture.nativeElement.querySelector(
      'button[type="button"]',
    ) as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();
    for (const element of fixture.nativeElement.querySelectorAll('input')) {
      expect(element.type).toBe('text');
      expect(element.autocomplete).toBe('new-password');
    }
    toggle.click();
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('#new-password') as HTMLInputElement).type).toBe(
      'password',
    );
    expect(auth.resetPassword).not.toHaveBeenCalled();
  });
});
