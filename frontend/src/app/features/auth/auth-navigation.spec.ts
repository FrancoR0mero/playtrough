import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { TokenStorageService } from './services/token-storage';
import { authDestination } from './auth-navigation';

describe('Authentication navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    TestBed.inject(TokenStorageService).clear();
  });
  afterEach(() => TestBed.inject(TokenStorageService).clear());

  it.each([
    null,
    'https://example.com',
    '//example.com',
    '/\\example.com',
    '/auth/login',
    '/auth/register?returnUrl=/auth/login',
    '/library(aux:auth/login)',
  ])('rejects unsafe or looping returnUrl %s', (value) => {
    expect(authDestination(TestBed.inject(Router), value)).toBe('/library');
  });

  it('preserves internal returnUrl query parameters and fragments', () => {
    expect(authDestination(TestBed.inject(Router), '/library?tab=playing#games')).toBe(
      '/library?tab=playing#games',
    );
  });

  it('redirects unauthenticated library visits to login with returnUrl', async () => {
    const harness = await RouterTestingHarness.create('/library?tab=playing');
    const url = TestBed.inject(Router).parseUrl(TestBed.inject(Router).url);
    expect(url.queryParams['returnUrl']).toBe('/library?tab=playing');
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
      'Iniciar sesión',
    );
  });

  it('restores a session for library and redirects away from auth on reload', async () => {
    TestBed.inject(TokenStorageService).save({
      accessToken: 'access',
      refreshToken: 'refresh',
      expiresAt: new Date(Date.now() + 900_000).toISOString(),
      user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
    });
    const harness = await RouterTestingHarness.create('/library');
    expect(harness.routeNativeElement?.textContent).toContain('Bienvenido, ana');
    await harness.navigateByUrl('/auth/login');
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/library');
  });
});
