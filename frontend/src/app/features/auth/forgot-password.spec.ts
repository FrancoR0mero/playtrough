import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { createAuthMock, createTokenStorageMock } from './auth-test-helpers';
import { ForgotPasswordComponent } from './forgot-password';
import type { AuthMessage } from './models/password-recovery';
import { AuthService } from './services/auth';
import { TokenStorageService } from './services/token-storage';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let response: Subject<AuthMessage>;

  beforeEach(() => {
    localStorage.clear();
    auth = createAuthMock();
    response = new Subject<AuthMessage>();
    auth.forgotPassword.mockReturnValue(response);
    TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: createTokenStorageMock() },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    response.complete();
    localStorage.clear();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  const button = () =>
    fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
  function email(value: string, touch = false): HTMLInputElement {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    if (touch) input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    return input;
  }
  function submit(): void {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }

  it('InvalidEmail_DisablesSubmitAndShowsErrorsOnlyAfterTouch', () => {
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    email('bad');
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    email('bad', true);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'no es válido',
    );
    email('', true);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'obligatorio',
    );
    expect(auth.forgotPassword).not.toHaveBeenCalled();
  });

  it('InvalidSubmission_FocusesEmail', () => {
    const focus = vi.spyOn(fixture.nativeElement.querySelector('input'), 'focus');
    submit();
    expect(focus).toHaveBeenCalled();
    expect(auth.forgotPassword).not.toHaveBeenCalled();
  });

  it.each(['Account found', 'Unknown account', ''])(
    'SuccessfulRequest_ShowsSameNeutralMessageRegardlessOfBody: %s',
    (message) => {
      email('ana@example.com');
      submit();
      expect(auth.forgotPassword).toHaveBeenCalledWith('ana@example.com');
      expect(button().disabled).toBe(true);
      expect(button().textContent).toContain('Enviando');
      submit();
      expect(auth.forgotPassword).toHaveBeenCalledTimes(1);
      response.next({ message });
      response.complete();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
        'Si el email está registrado, te enviamos un link para restablecer tu contraseña.',
      );
      expect(button().disabled).toBe(true);
      expect((fixture.nativeElement.querySelector('input') as HTMLInputElement).readOnly).toBe(
        true,
      );
    },
  );

  it('SuccessfulRequest_DisablesResendForThirtySecondsThenAllowsAnotherRequest', () => {
    vi.useFakeTimers();
    email('ana@example.com');
    submit();
    response.next({ message: 'ok' });
    response.complete();
    fixture.detectChanges();
    expect(button().textContent).toContain('30 s');
    vi.advanceTimersByTime(29_000);
    fixture.detectChanges();
    expect(button().disabled).toBe(true);
    expect(button().textContent).toContain('1 s');
    submit();
    expect(auth.forgotPassword).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1_000);
    fixture.detectChanges();
    expect(button().disabled).toBe(false);
    expect(button().textContent).toContain('Reenviar enlace');
    response = new Subject<AuthMessage>();
    auth.forgotPassword.mockReturnValue(response);
    submit();
    expect(auth.forgotPassword).toHaveBeenCalledTimes(2);
    expect(button().disabled).toBe(true);
  });

  it.each([0, 500])('FailedRequest_%s_ShowsAccessibleGenericErrorAndAllowsRetry', (status) => {
    email('ana@example.com');
    submit();
    response.error(new HttpErrorResponse({ status }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'No pudimos procesar la solicitud',
    );
    expect(button().disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
    response = new Subject<AuthMessage>();
    auth.forgotPassword.mockReturnValue(response);
    submit();
    expect(auth.forgotPassword).toHaveBeenCalledTimes(2);
  });

  it('BackLink_LeadsToLoginWithoutQueryParameters', () => {
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe('/auth/login');
  });
});
