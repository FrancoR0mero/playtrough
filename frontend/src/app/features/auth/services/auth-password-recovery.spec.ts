import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { AuthService } from './auth';
import { TokenStorageService } from './token-storage';
import { createSession } from '../auth-test-helpers';

describe('AuthService password recovery', () => {
  let http: HttpTestingController;
  let auth: AuthService;
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(TokenStorageService).save(createSession());
    auth = TestBed.inject(AuthService);
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      TestBed.resetTestingModule();
      localStorage.clear();
    }
  });

  it('ForgotPassword_PostsEmailWithoutAuthorization_EvenWithActiveSession', () => {
    const next = vi.fn();
    auth.forgotPassword(' ana@example.com ').subscribe(next);
    const request = http.expectOne(`${environment.apiUrl}/auth/forgot-password`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'ana@example.com' });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ message: 'neutral' });
    expect(next).toHaveBeenCalledWith({ message: 'neutral' });
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('ResetPassword_PostsAllFourFieldsWithoutAuthorization', () => {
    const next = vi.fn();
    auth
      .resetPassword('ana@example.com', 'encoded-token', 'NuevaClave123', 'NuevaClave123')
      .subscribe(next);
    const request = http.expectOne(`${environment.apiUrl}/auth/reset-password`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      email: 'ana@example.com',
      token: 'encoded-token',
      newPassword: 'NuevaClave123',
      confirmPassword: 'NuevaClave123',
    });
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ message: 'updated' });
    expect(next).toHaveBeenCalledWith({ message: 'updated' });
  });

  it.each([
    {
      title: 'Invalid password reset request',
      detail: 'The password reset link is invalid or has expired.',
    },
    { errors: { NewPassword: ['Passwords must be at least 8 characters.'] } },
  ])('ResetPassword_PreservesBadRequestDetails: %j', (problem) => {
    const error = vi.fn();
    auth.resetPassword('ana@example.com', 'token', 'password', 'password').subscribe({ error });
    http
      .expectOne(`${environment.apiUrl}/auth/reset-password`)
      .flush(problem, { status: 400, statusText: 'Bad Request' });
    expect(error.mock.calls[0][0]).toBeInstanceOf(HttpErrorResponse);
    expect(error.mock.calls[0][0].error).toEqual(problem);
  });

  it('ForgotPassword_PropagatesValidationDetails', () => {
    const error = vi.fn();
    const problem = { errors: { Email: ['A valid email address is required.'] } };
    auth.forgotPassword('invalid').subscribe({ error });
    http
      .expectOne(`${environment.apiUrl}/auth/forgot-password`)
      .flush(problem, { status: 400, statusText: 'Bad Request' });
    expect(error.mock.calls[0][0].error).toEqual(problem);
  });

  it('ClearSession_RemovesStorageAndReactiveUser', () => {
    auth.clearSession();
    expect(localStorage.length).toBe(0);
    expect(auth.currentUser()).toBeNull();
    expect(auth.isAuthenticated()).toBe(false);
  });
});
