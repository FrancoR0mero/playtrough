import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { createAuthMock, createTokenStorageMock } from './auth-test-helpers';
import type { AuthMessage } from './models/password-recovery';
import { ResendConfirmationComponent } from './resend-confirmation';
import { AuthService } from './services/auth';
import { TokenStorageService } from './services/token-storage';

describe('ResendConfirmationComponent', () => {
  let fixture: ComponentFixture<ResendConfirmationComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let response: Subject<AuthMessage>;
  beforeEach(() => {
    localStorage.clear();
    auth = createAuthMock();
    response = new Subject<AuthMessage>();
    auth.resendConfirmation.mockReturnValue(response);
    TestBed.configureTestingModule({
      imports: [ResendConfirmationComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: createTokenStorageMock() },
      ],
    });
    fixture = TestBed.createComponent(ResendConfirmationComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    fixture.destroy();
    response.complete();
    localStorage.clear();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });
  const button = () => fixture.nativeElement.querySelector('button') as HTMLButtonElement;
  function email(value: string) {
    const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
  }
  function submit() {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }

  it('Resend_WithInvalidEmail_ShowsAccessibleValidationAndDoesNotSend', () => {
    // Arrange / Act
    email('invalid');
    submit();
    // Assert
    expect(button().disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'no es válido',
    );
    expect(auth.resendConfirmation).not.toHaveBeenCalled();
  });

  it('Resend_WithPendingRequest_IgnoresRepeatedSubmissions', () => {
    // Arrange
    email('ana@example.com');
    // Act
    submit();
    submit();
    // Assert
    expect(auth.resendConfirmation).toHaveBeenCalledExactlyOnceWith('ana@example.com');
    expect(button().disabled).toBe(true);
    expect(button().textContent).toContain('Enviando');
  });

  it('Resend_WithSuccess_WaitsSixtySecondsAndCleansTimerOnDestroy', () => {
    // Arrange
    vi.useFakeTimers();
    email('ana@example.com');
    submit();
    // Act
    response.next({ message: 'account-specific-message' });
    response.complete();
    fixture.detectChanges();
    // Assert
    expect(button().disabled).toBe(true);
    expect(button().textContent).toContain('60 s');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'Si el email corresponde',
    );
    expect(fixture.nativeElement.textContent).not.toContain('account-specific-message');
    vi.advanceTimersByTime(59_000);
    fixture.detectChanges();
    submit();
    expect(button().disabled).toBe(true);
    expect(auth.resendConfirmation).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(button().disabled).toBe(false);
    response = new Subject<AuthMessage>();
    auth.resendConfirmation.mockReturnValue(response);
    submit();
    response.next({ message: 'ok' });
    response.complete();
    fixture.detectChanges();
    expect(auth.resendConfirmation).toHaveBeenCalledTimes(2);
    fixture.destroy();
    expect(vi.getTimerCount()).toBe(0);
  });

  it.each([0, 400, 500])('Resend_WithFailure_%s_ShowsAlertAndAllowsRetry', (status) => {
    // Arrange
    email('ana@example.com');
    submit();
    // Act
    response.error(new HttpErrorResponse({ status }));
    fixture.detectChanges();
    // Assert
    expect(button().disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'No pudimos procesar',
    );
    expect(fixture.nativeElement.querySelector('[role="status"]')).toBeNull();
  });
});
