import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { createAuthMock, createSession, createTokenStorageMock } from './auth-test-helpers';
import type { LoginResponse } from './models/login';
import { RegisterComponent } from './register';
import { AuthService, LoginError } from './services/auth';
import { TokenStorageService } from './services/token-storage';

describe('Login form', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let storage: ReturnType<typeof createTokenStorageMock>;
  let loginResult: Subject<LoginResponse>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let mode: BehaviorSubject<{ mode: string }>;

  beforeEach(() => {
    localStorage.removeItem('playtrough.session');
    auth = createAuthMock();
    storage = createTokenStorageMock();
    loginResult = new Subject<LoginResponse>();
    auth.login.mockImplementation(() => loginResult.asObservable());
    mode = new BehaviorSubject({ mode: 'login' });
  });

  afterEach(() => {
    fixture?.destroy();
    loginResult.complete();
    mode.complete();
    localStorage.removeItem('playtrough.session');
    vi.restoreAllMocks();
  });

  async function render(
    returnUrl: string | null = null,
    sessionExpired: string | null = null,
  ): Promise<void> {
    const params = convertToParamMap({
      ...(returnUrl ? { returnUrl } : {}),
      ...(sessionExpired ? { sessionExpired } : {}),
    });
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: storage },
        {
          provide: ActivatedRoute,
          useValue: {
            data: mode.asObservable(),
            queryParamMap: of(params),
            snapshot: { data: { mode: 'login' }, queryParamMap: params },
          },
        },
      ],
    });
    // Keep the pure URL parser used by RouterLink, but prevent real navigation.
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function input(name: string, value: string): HTMLInputElement {
    const element = fixture.nativeElement.querySelector(
      `[formControlName="${name}"]`,
    ) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    return element;
  }

  function submit(): void {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }

  function credentials(): void {
    input('identifier', 'ana');
    input('password', 'x');
  }

  function expirationNotice(): HTMLElement | undefined {
    return Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('[role="status"], [role="alert"]'),
    ).find((element) => element.textContent?.includes('Tu sesión expiró'));
  }

  it('shows an accessible expired-session notice when the query indicator is true', async () => {
    // Arrange / Act
    await render(null, 'true');

    // Assert
    const notice = expirationNotice();
    expect(notice?.getAttribute('role')).toBe('status');
    expect(notice?.textContent).toContain('Tu sesión expiró, iniciá sesión de nuevo');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it.each([null, 'false', '1'])(
    'does not show the expired-session notice for indicator %s',
    async (indicator) => {
      // Arrange / Act
      await render(null, indicator);

      // Assert
      expect(expirationNotice()).toBeUndefined();
    },
  );

  it.each([false, true])(
    'dismisses the expired-session notice on submit (valid=$0)',
    async (valid) => {
      // Arrange
      await render(null, 'true');
      if (valid) credentials();
      expect(expirationNotice()).toBeDefined();

      // Act
      submit();

      // Assert
      expect(expirationNotice()).toBeUndefined();
      if (valid) {
        loginResult.error(new LoginError({}, 'Credenciales incorrectas'));
        await fixture.whenStable();
        fixture.detectChanges();
        expect(expirationNotice()).toBeUndefined();
        expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
          'Credenciales incorrectas',
        );
      } else expect(auth.login).not.toHaveBeenCalled();
    },
  );

  it('hides the expired-session notice on registration and omits the indicator from the mode link', async () => {
    // Arrange
    await render(null, 'true');
    expect(expirationNotice()).toBeDefined();
    const link = fixture.nativeElement.querySelector('a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).not.toContain('sessionExpired');

    // Act
    mode.next({ mode: 'register' });
    fixture.detectChanges();
    await fixture.whenStable();

    // Assert
    expect(expirationNotice()).toBeUndefined();
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Crear cuenta');
  });

  it('shows required errors only after each field is touched and accepts a short login password', async () => {
    // Arrange
    await render();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();

    // Act
    const identifier = fixture.nativeElement.querySelector('#identifier') as HTMLInputElement;
    identifier.dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    // Assert
    const identifierError = fixture.nativeElement.querySelector('#identifier-error');
    expect(identifierError?.textContent).toContain('obligatorio');
    expect(identifierError?.getAttribute('role')).toBe('alert');
    expect(fixture.nativeElement.querySelector('#login-password-error')).toBeNull();
    expect(identifier.getAttribute('aria-describedby')).toBe('identifier-error');
    const password = fixture.nativeElement.querySelector('#login-password') as HTMLInputElement;
    password.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#login-password-error')?.getAttribute('role')).toBe(
      'alert',
    );
    credentials();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
    expect(identifier.autocomplete).toBe('username');
    expect(password.autocomplete).toBe('current-password');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('disables submission whenever either required field is empty', async () => {
    // Arrange
    await render();
    const button = fixture.nativeElement.querySelector(
      'button[type="submit"]',
    ) as HTMLButtonElement;

    // Act / Assert
    expect(button.disabled).toBe(true);
    input('identifier', 'ana');
    expect(button.disabled).toBe(true);
    input('password', 'x');
    expect(button.disabled).toBe(false);
    input('identifier', '');
    expect(button.disabled).toBe(true);
    input('identifier', 'ana');
    input('password', '');
    expect(button.disabled).toBe(true);
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('toggles password visibility without submitting or changing the value', async () => {
    // Arrange
    await render();
    const password = input('password', 'secret');
    const toggle = fixture.nativeElement.querySelector(
      'button[aria-controls="login-password"]',
    ) as HTMLButtonElement;

    // Act / Assert
    expect(toggle.type).toBe('button');
    toggle.click();
    fixture.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    toggle.click();
    fixture.detectChanges();
    expect(password.type).toBe('password');
    expect(password.value).toBe('secret');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('calls login with the form values, shows loading and blocks duplicate submission', async () => {
    // Arrange
    await render();
    credentials();

    // Act
    submit();
    submit();

    // Assert
    expect(auth.login).toHaveBeenCalledExactlyOnceWith('ana', 'x');
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('form').getAttribute('aria-busy')).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Ingresando');
    expect(navigate).not.toHaveBeenCalled();
    expect(storage.save).not.toHaveBeenCalled();
  });

  it.each([null, '/library?tab=playing'])(
    'redirects successful login to the default route or returnUrl %s',
    async (returnUrl) => {
      // Arrange
      await render(returnUrl);
      credentials();
      submit();

      // Act
      loginResult.next(createSession());
      loginResult.complete();
      await fixture.whenStable();

      // Assert
      expect(navigate).toHaveBeenCalledExactlyOnceWith(returnUrl ?? '/library', {
        replaceUrl: true,
      });
    },
  );

  it.each([
    { status: 401, message: 'Credenciales incorrectas' },
    {
      status: 423,
      message: 'Tu cuenta está bloqueada temporalmente. Intentá iniciar sesión más tarde.',
    },
    { status: 500, message: 'Ocurrió un error inesperado. Inténtalo de nuevo.' },
    { status: 0, message: 'Ocurrió un error inesperado. Inténtalo de nuevo.' },
  ])('shows an accessible generic error for $status and allows retry', async ({ message }) => {
    // Arrange
    await render();
    credentials();
    submit();

    // Act
    loginResult.error(new LoginError({}, message));
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain(message);
    expect(fixture.nativeElement.querySelector('#identifier-error')).toBeNull();
    expect(fixture.nativeElement.querySelector('#login-password-error')).toBeNull();
    expect(
      fixture.nativeElement.querySelector('#identifier').getAttribute('aria-invalid'),
    ).toBeNull();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
    loginResult = new Subject<LoginResponse>();
    submit();
    loginResult.next(createSession());
    loginResult.complete();
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/library', { replaceUrl: true });
  });

  it('shows server field validation, focuses the first error and clears it on edit', async () => {
    // Arrange
    await render();
    credentials();
    submit();

    // Act
    loginResult.error(
      new LoginError(
        {
          identifier: 'El email o nombre de usuario es obligatorio.',
          password: 'Mensaje específico del servidor.',
        },
        null,
      ),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    const identifier = fixture.nativeElement.querySelector('#identifier') as HTMLInputElement;
    expect(document.activeElement).toBe(identifier);
    expect(identifier.getAttribute('aria-invalid')).toBe('true');
    expect(fixture.nativeElement.querySelector('#login-password-error').textContent).toContain(
      'Mensaje específico',
    );
    input('identifier', 'ana2');
    expect(fixture.nativeElement.querySelector('#identifier-error')).toBeNull();
  });

  it('shows validation errors without a recognized field in an alert', async () => {
    // Arrange
    await render();
    credentials();
    submit();

    // Act
    loginResult.error(new LoginError({}, 'Solicitud no válida.'));
    await fixture.whenStable();
    fixture.detectChanges();

    // Assert
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Solicitud no válida.',
    );
  });

  it('focuses the first invalid field without calling login on invalid submission', async () => {
    // Arrange
    await render();

    // Act
    submit();

    // Assert
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#identifier'));
    expect(auth.login).not.toHaveBeenCalled();
  });

  it.each([null, '/library?tab=playing'])(
    'redirects an active session away from login with returnUrl %s',
    async (returnUrl) => {
      // Arrange
      auth.isAuthenticated.set(true);
      auth.currentUser.set(createSession().user);

      // Act
      await render(returnUrl);

      // Assert
      expect(auth.restoreSession).toHaveBeenCalledOnce();
      expect(navigate).toHaveBeenCalledWith(returnUrl ?? '/library', { replaceUrl: true });
      expect(auth.login).not.toHaveBeenCalled();
    },
  );
});
