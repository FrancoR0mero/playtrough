import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import type { RegisterRequest, RegisterResponse } from '../models/register';
import { AuthService, RegisterError } from './auth';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const payload: RegisterRequest = {
    email: 'ana@example.com',
    username: 'ana_dev',
    password: 'Str0ngPass1',
  };

  const createdResponse: RegisterResponse = {
    id: 1,
    username: 'ana_dev',
    email: 'ana@example.com',
    createdAt: '2026-10-11T12:00:00.000Z',
  };

  const registerUrl = `${environment.apiUrl}/auth/register`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('posts the payload and returns the mapped 201 response', () => {
    let response: RegisterResponse | undefined;
    service.register(payload).subscribe((value) => (response = value));

    const request = httpMock.expectOne(registerUrl);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(payload);

    request.flush(createdResponse, { status: 201, statusText: 'Created' });

    expect(response).toEqual(createdResponse);
  });

  it('maps a 400 validation problem to a Spanish field error', () => {
    let error: RegisterError | undefined;
    service.register(payload).subscribe({ error: (value: unknown) => (error = value as RegisterError) });

    const request = httpMock.expectOne(registerUrl);
    request.flush(
      { errors: { Password: ["Passwords must have at least one uppercase ('A'-'Z')."] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(error).toBeInstanceOf(RegisterError);
    expect(error?.fieldErrors['password']).toBe('Debe incluir al menos una mayúscula.');
  });

  it('maps a 409 email conflict to a Spanish message', () => {
    let error: RegisterError | undefined;
    service.register(payload).subscribe({ error: (value: unknown) => (error = value as RegisterError) });

    const request = httpMock.expectOne(registerUrl);
    request.flush({ title: 'Email already registered' }, { status: 409, statusText: 'Conflict' });

    expect(error).toBeInstanceOf(RegisterError);
    expect(error?.formError).toBe('El email ya está registrado.');
  });

  it('maps a 409 username conflict to a Spanish message', () => {
    let error: RegisterError | undefined;
    service.register(payload).subscribe({ error: (value: unknown) => (error = value as RegisterError) });

    const request = httpMock.expectOne(registerUrl);
    request.flush({ title: 'Username already taken' }, { status: 409, statusText: 'Conflict' });

    expect(error).toBeInstanceOf(RegisterError);
    expect(error?.formError).toBe('El nombre de usuario ya está en uso.');
  });

  it('maps a 500 error to a generic Spanish message', () => {
    let error: RegisterError | undefined;
    service.register(payload).subscribe({ error: (value: unknown) => (error = value as RegisterError) });

    const request = httpMock.expectOne(registerUrl);
    request.flush(
      { title: 'An unexpected error occurred while registering the account.' },
      { status: 500, statusText: 'Internal Server Error' },
    );

    expect(error).toBeInstanceOf(RegisterError);
    expect(error?.formError).toBe('Ocurrió un error inesperado. Inténtalo de nuevo.');
  });
});
