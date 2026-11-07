import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { ConfirmEmailComponent } from './confirm-email';
import { ForgotPasswordComponent } from './forgot-password';
import { RegisterComponent } from './register';
import { ResendConfirmationComponent } from './resend-confirmation';
import { ResetPasswordComponent } from './reset-password';
import { AuthService } from './services/auth';

const SCREENS: {
  name: string;
  component: Type<unknown>;
  endpoint: string;
  fields: Record<string, string>;
  mode?: string;
  automatic?: boolean;
}[] = [
  {
    name: 'login',
    component: RegisterComponent,
    endpoint: 'login',
    mode: 'login',
    fields: { identifier: 'ana@example.com', password: 'Str0ngPass123' },
  },
  {
    name: 'register',
    component: RegisterComponent,
    endpoint: 'register',
    mode: 'register',
    fields: { email: 'ana@example.com', username: 'ana_dev', password: 'Str0ngPass123' },
  },
  {
    name: 'forgot password',
    component: ForgotPasswordComponent,
    endpoint: 'forgot-password',
    fields: { email: 'ana@example.com' },
  },
  {
    name: 'resend confirmation',
    component: ResendConfirmationComponent,
    endpoint: 'resend-confirmation',
    fields: { email: 'ana@example.com' },
  },
  {
    name: 'reset password',
    component: ResetPasswordComponent,
    endpoint: 'reset-password',
    fields: { newPassword: 'Str0ngPass123', confirmPassword: 'Str0ngPass123' },
  },
  {
    name: 'confirm email',
    component: ConfirmEmailComponent,
    endpoint: 'confirm-email',
    fields: {},
    automatic: true,
  },
];

describe.each(SCREENS)('Rate limiting in $name', (screen) => {
  let fixture: ComponentFixture<unknown>;
  let httpMock: HttpTestingController;
  const endpoint = `${environment.apiUrl}/auth/${screen.endpoint}`;

  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    const params = convertToParamMap({ email: 'ana@example.com', token: 'original-link-token' });
    TestBed.configureTestingModule({
      imports: [screen.component],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            data: of({ mode: screen.mode }),
            queryParamMap: of(params),
            snapshot: { data: { mode: screen.mode }, queryParamMap: params },
          },
        },
      ],
    });
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(screen.component);
    fixture.detectChanges();
    for (const [name, value] of Object.entries(screen.fields)) {
      const input = fixture.nativeElement.querySelector(
        `[formControlName="${name}"]`,
      ) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    fixture.detectChanges();
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      fixture.destroy();
      TestBed.resetTestingModule();
      localStorage.clear();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  const button = () =>
    fixture.nativeElement.querySelector(
      screen.automatic ? 'button' : 'button[type="submit"]',
    ) as HTMLButtonElement;
  function submit() {
    if (screen.automatic) button().dispatchEvent(new Event('click'));
    else
      fixture.nativeElement
        .querySelector('form')
        .dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();
  }

  it.each(['90', undefined])(
    'shows an alert and blocks submissions until Retry-After=%s expires',
    (header) => {
      const auth = TestBed.inject(AuthService);
      const refresh = vi.spyOn(auth, 'refresh');
      const logout = vi.spyOn(auth, 'logout');
      const clear = vi.spyOn(auth, 'clearSession');
      const navigate = TestBed.inject(Router).navigateByUrl;
      if (!screen.automatic) submit();
      const request = httpMock.expectOne(endpoint);
      const originalBody = request.request.body;
      request.flush(
        { code: 'rate_limited' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: header ? { 'Retry-After': header } : {},
        },
      );
      fixture.detectChanges();
      const seconds = header ? 90 : 60;
      const message = header ? '1 minuto y 30 segundos' : '60 segundos';
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        `Demasiados intentos. Probá de nuevo en ${message}`,
      );
      expect(button().disabled).toBe(true);
      expect(button().textContent).toContain(`${seconds} s`);
      expect(refresh).not.toHaveBeenCalled();
      expect(logout).not.toHaveBeenCalled();
      expect(clear).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();

      submit();
      httpMock.expectNone(endpoint);
      vi.advanceTimersByTime((seconds - 1) * 1_000);
      fixture.detectChanges();
      expect(button().disabled).toBe(true);
      expect(button().textContent).toContain('1 s');
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        '1 segundo',
      );
      submit();
      httpMock.expectNone(endpoint);

      vi.advanceTimersByTime(1_000);
      fixture.detectChanges();
      expect(button().disabled).toBe(false);
      expect(fixture.nativeElement.textContent).not.toContain('Demasiados intentos');
      httpMock.expectNone(endpoint); // Expiry never retries automatically.
      submit();
      const retry = httpMock.expectOne(endpoint);
      expect(retry.request.body).toEqual(originalBody);
      retry.flush(null, { status: 500, statusText: 'Error' });
    },
  );
});
