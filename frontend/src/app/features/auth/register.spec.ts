import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { LoginResponse } from './models/login';
import { RegisterComponent } from './register';
import { TokenStorageService } from './services/token-storage';

describe('Authentication form', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let storage: TokenStorageService;
  const session = (): LoginResponse => ({
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  });

  async function render(mode = 'login', returnUrl: string | null = null): Promise<void> {
    const params = convertToParamMap(returnUrl ? { returnUrl } : {});
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            data: of({ mode }),
            queryParamMap: of(params),
            snapshot: { data: { mode }, queryParamMap: params },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
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

  beforeEach(() => {
    storage = new TokenStorageService();
    storage.clear();
  });
  afterEach(() => {
    http?.verify();
    fixture?.destroy();
    storage.clear();
    vi.restoreAllMocks();
  });

  it('shows errors only after touch and accepts a short password in login', async () => {
    await render();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    const identifier = fixture.nativeElement.querySelector('#identifier') as HTMLInputElement;
    identifier.dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#identifier-error')?.textContent).toContain(
      'obligatorio',
    );
    expect(fixture.nativeElement.querySelector('#login-password-error')).toBeNull();
    expect(identifier.getAttribute('aria-describedby')).toBe('identifier-error');
    credentials();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
    expect(identifier.autocomplete).toBe('username');
    expect(fixture.nativeElement.querySelector('#login-password').autocomplete).toBe(
      'current-password',
    );
  });

  it('shows/hides the password without submitting or changing its value', async () => {
    await render();
    const password = input('password', 'secret');
    const toggle = fixture.nativeElement.querySelector(
      'button[aria-controls="login-password"]',
    ) as HTMLButtonElement;
    expect(toggle.type).toBe('button');
    toggle.click();
    fixture.detectChanges();
    expect(password.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    toggle.click();
    fixture.detectChanges();
    expect(password.type).toBe('password');
    expect(password.value).toBe('secret');
    http.expectNone(`${environment.apiUrl}/auth/login`);
  });

  it('submits the form, blocks duplicate requests, shows loading and redirects on success', async () => {
    await render('login', '/library?tab=playing');
    credentials();
    submit();
    submit();
    expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    expect(fixture.nativeElement.querySelector('form').getAttribute('aria-busy')).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Ingresando');
    const req = http.expectOne(`${environment.apiUrl}/auth/login`);
    expect(req.request.body).toEqual({ identifier: 'ana', password: 'x' });
    req.flush(session());
    await fixture.whenStable();
    expect(navigate).toHaveBeenCalledWith('/library?tab=playing', { replaceUrl: true });
  });

  it.each([401, 423, 500, 0])(
    'shows an accessible error and allows retry after status %s',
    async (status) => {
      await render();
      credentials();
      submit();
      const req = http.expectOne(`${environment.apiUrl}/auth/login`);
      if (status === 0) req.error(new ProgressEvent('error'));
      else req.flush({ detail: 'Internal server information' }, { status, statusText: 'Error' });
      await fixture.whenStable();
      fixture.detectChanges();
      const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
      expect(alert.textContent).toContain(
        status === 401
          ? 'Credenciales incorrectas'
          : status === 423
            ? 'bloqueada temporalmente'
            : 'Inténtalo de nuevo',
      );
      expect(alert.textContent).not.toContain('Internal server information');
      expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
      expect(
        fixture.nativeElement.querySelector('#identifier').getAttribute('aria-invalid'),
      ).toBeNull();
      submit();
      http.expectOne(`${environment.apiUrl}/auth/login`).flush(session());
      await fixture.whenStable();
      expect(navigate).toHaveBeenCalledWith('/library', { replaceUrl: true });
    },
  );

  it('displays server field validation, focuses the first error and clears it on edit', async () => {
    await render();
    credentials();
    submit();
    http.expectOne(`${environment.apiUrl}/auth/login`).flush(
      {
        errors: {
          Identifier: ['Email or username is required.'],
          Password: ['Mensaje específico del servidor.'],
        },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();
    const identifier = fixture.nativeElement.querySelector('#identifier') as HTMLInputElement;
    expect(document.activeElement).toBe(identifier);
    expect(identifier.getAttribute('aria-invalid')).toBe('true');
    expect(fixture.nativeElement.querySelector('#login-password-error').textContent).toContain(
      'Mensaje específico',
    );
    input('identifier', 'ana2');
    expect(fixture.nativeElement.querySelector('#identifier-error')).toBeNull();
  });

  it('does not discard validation errors without a recognized field', async () => {
    await render();
    credentials();
    submit();
    http
      .expectOne(`${environment.apiUrl}/auth/login`)
      .flush(
        { errors: { '': ['Solicitud no válida.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Solicitud no válida.',
    );
  });

  it('focuses the first invalid field on an invalid form submission', async () => {
    await render();
    submit();
    expect(document.activeElement).toBe(fixture.nativeElement.querySelector('#identifier'));
    http.expectNone(`${environment.apiUrl}/auth/login`);
  });

  it.each([null, '/library?tab=playing'])(
    'redirects a successful registration to login, preserving returnUrl %s',
    async (returnUrl) => {
      await render('register', returnUrl);
      input('email', 'ana@example.com');
      input('username', 'ana');
      input('password', 'x');
      expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
      input('password', 'Str0ngPass123');
      submit();
      http
        .expectOne(`${environment.apiUrl}/auth/register`)
        .flush(
          { id: 1, username: 'ana', email: 'ana@example.com', createdAt: new Date().toISOString() },
          { status: 201, statusText: 'Created' },
        );
      await fixture.whenStable();
      fixture.detectChanges();
      const target = returnUrl ? '/auth/login?returnUrl=%2Flibrary%3Ftab%3Dplaying' : '/auth/login';
      expect(navigate).toHaveBeenCalledWith(target, { replaceUrl: true });
      expect(storage.read()).toBeNull();
    },
  );

  it('stays in registration when account creation fails', async () => {
    await render('register');
    input('email', 'ana@example.com');
    input('username', 'ana');
    input('password', 'Str0ngPass123');
    submit();
    http
      .expectOne(`${environment.apiUrl}/auth/register`)
      .flush({ title: 'Email already registered' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'El email ya está registrado.',
    );
  });

  it.each(['login', 'register'])('redirects an active session away from %s', async (mode) => {
    storage.save(session());
    await render(mode);
    expect(navigate).toHaveBeenCalledWith('/library', { replaceUrl: true });
  });
});
