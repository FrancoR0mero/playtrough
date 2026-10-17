import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { createSession } from '../auth-test-helpers';
import { AuthService, LoginError } from './auth';
import { TokenStorageService } from './token-storage';

describe('AuthService email confirmation', () => {
  let auth: AuthService;
  let httpMock: HttpTestingController;
  let storage: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigateByUrl: vi.fn().mockResolvedValue(true) } },
      ],
    });
    const tokens = TestBed.inject(TokenStorageService);
    tokens.save(createSession());
    storage = vi.spyOn(tokens, 'save');
    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      TestBed.resetTestingModule();
      localStorage.clear();
      vi.restoreAllMocks();
    }
  });

  it('ConfirmEmail_PostsTypedPayloadWithoutAuthorizationAndPreservesActiveSession', () => {
    // Arrange
    const next = vi.fn();
    const before = localStorage.getItem('playtrough.session');
    // Act
    auth.confirmEmail(' ana@example.com ', 'url-safe-token').subscribe(next);
    const request = httpMock.expectOne(`${environment.apiUrl}/auth/confirm-email`);
    // Assert
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ana@example.com', token: 'url-safe-token' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ message: 'confirmed' });
    expect(next).toHaveBeenCalledWith({ message: 'confirmed' });
    expect(localStorage.getItem('playtrough.session')).toBe(before);
    expect(auth.isAuthenticated()).toBe(true);
    expect(storage).not.toHaveBeenCalled();
  });

  it('ResendConfirmation_PostsTypedEmailWithoutAuthorization', () => {
    // Arrange
    const next = vi.fn();
    // Act
    auth.resendConfirmation(' ana@example.com ').subscribe(next);
    const request = httpMock.expectOne(`${environment.apiUrl}/auth/resend-confirmation`);
    // Assert
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ana@example.com' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ message: 'neutral' });
    expect(next).toHaveBeenCalledWith({ message: 'neutral' });
    expect(storage).not.toHaveBeenCalled();
  });

  it.each([
    { status: 403, body: { code: 'email_not_confirmed' }, code: 'email_not_confirmed' },
    {
      status: 403,
      body: { extensions: { code: 'email_not_confirmed' } },
      code: 'email_not_confirmed',
    },
    { status: 401, body: { code: 'email_not_confirmed' }, code: null },
    { status: 403, body: { detail: 'email_not_confirmed' }, code: null },
    { status: 403, body: { code: 'other' }, code: null },
  ])('Login_DistinguishesConfirmationByStatusAndCode: $status $body', ({ status, body, code }) => {
    // Arrange
    const error = vi.fn();
    // Act
    auth.login('ana@example.com', 'password').subscribe({ error });
    httpMock
      .expectOne(`${environment.apiUrl}/auth/login`)
      .flush(body, { status, statusText: 'Error' });
    // Assert
    const received = error.mock.calls[0][0] as LoginError;
    expect(received).toBeInstanceOf(LoginError);
    expect(received.code).toBe(code);
    expect(received.formError).toBe(
      code
        ? 'Tenés que confirmar tu email antes de entrar'
        : status === 401
          ? 'Credenciales incorrectas'
          : 'Ocurrió un error inesperado. Inténtalo de nuevo.',
    );
    expect(storage).not.toHaveBeenCalled();
  });
});
