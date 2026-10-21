import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { createAuthMock, createSession, createTokenStorageMock } from './auth-test-helpers';
import type { RegisterResponse } from './models/register';
import { RegisterComponent } from './register';
import { AuthService, RegisterError } from './services/auth';
import { TokenStorageService } from './services/token-storage';

describe('Registration form', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let storage: ReturnType<typeof createTokenStorageMock>;
  let response: Subject<RegisterResponse>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    auth = createAuthMock();
    storage = createTokenStorageMock();
    response = new Subject<RegisterResponse>();
    auth.register.mockReturnValue(response);
  });
  afterEach(() => {
    fixture?.destroy();
    response.complete();
    localStorage.clear();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  async function render(returnUrl: string | null = null) {
    const params = convertToParamMap(returnUrl ? { returnUrl } : {});
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: storage },
        {
          provide: ActivatedRoute,
          useValue: {
            data: of({ mode: 'register' }),
            queryParamMap: of(params),
            snapshot: { data: { mode: 'register' }, queryParamMap: params },
          },
        },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function input(name: string, value: string) {
    const control = fixture.nativeElement.querySelector(
      `[formControlName="${name}"]`,
    ) as HTMLInputElement;
    control.value = value;
    control.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
  }
  function submit() {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }
  function register() {
    input('email', 'ana@example.com');
    input('username', 'ana');
    input('password', 'Str0ngPass123');
    submit();
    response.next({
      id: 1,
      username: 'ana',
      email: 'ana@example.com',
      createdAt: new Date().toISOString(),
      emailConfirmationRequired: true,
    });
    response.complete();
    fixture.detectChanges();
  }

  it.each([null, '/library?tab=playing'])(
    'Register_WithSuccess_ShowsEmailWithoutSessionOrNavigationAndPreservesLoginReturnUrl: %s',
    async (returnUrl) => {
      // Arrange
      await render(returnUrl);
      // Act
      register();
      // Assert
      expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Revisá tu correo');
      expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
        'ana@example.com',
      );
      expect(fixture.nativeElement.textContent).toContain('spam');
      expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(
        returnUrl ? '/auth/login?returnUrl=%2Flibrary%3Ftab%3Dplaying' : '/auth/login',
      );
      expect(storage.save).not.toHaveBeenCalled();
      expect(auth.login).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    },
  );

  it('Register_WithSuccess_ResendsWithNeutralMessageAndSixtySecondCooldown', async () => {
    // Arrange
    await render();
    vi.useFakeTimers();
    auth.resendConfirmation.mockReturnValue(of({ message: 'private backend detail' }));
    register();
    const button = fixture.nativeElement.querySelector(
      'app-resend-confirmation button',
    ) as HTMLButtonElement;
    // Act
    button.click();
    fixture.detectChanges();
    // Assert
    expect(auth.resendConfirmation).toHaveBeenCalledExactlyOnceWith('ana@example.com');
    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('60 s');
    expect(fixture.nativeElement.textContent).toContain(
      'Si el email corresponde a una cuenta pendiente',
    );
    expect(fixture.nativeElement.textContent).not.toContain('private backend detail');
    vi.advanceTimersByTime(59_000);
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain('1 s');
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(button.disabled).toBe(false);
  });

  it('Register_WithFailure_PreservesFormAndShowsAccessibleError', async () => {
    // Arrange
    await render();
    input('email', 'ana@example.com');
    input('username', 'ana');
    input('password', 'Str0ngPass123');
    submit();
    // Act
    response.error(new RegisterError({}, 'El email ya está registrado.'));
    fixture.detectChanges();
    // Assert
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'El email ya está registrado.',
    );
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Crear cuenta');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('Register_WithActiveSession_PreservesExistingRedirect', async () => {
    // Arrange
    auth.isAuthenticated.set(true);
    auth.currentUser.set(createSession().user);
    // Act
    await render();
    // Assert
    expect(navigate).toHaveBeenCalledWith('/library', { replaceUrl: true });
  });
});
