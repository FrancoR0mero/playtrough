import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from '../../app.routes';
import { TokenStorageService } from './services/token-storage';
import { authDestination } from './auth-navigation';
import { RegisterComponent } from './register';
import { of } from 'rxjs';
import { LibraryService } from '../library/services/library';
import { EMPTY_LIBRARY } from '../library/library-test-helpers';

describe('Authentication navigation', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter(routes)],
    });
    TestBed.inject(TokenStorageService).clear();
    vi.spyOn(TestBed.inject(LibraryService), 'list').mockReturnValue(of(EMPTY_LIBRARY));
  });
  afterEach(() => TestBed.inject(TokenStorageService).clear());

  const session = () => ({
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  });

  it.each(['/auth/login', '/auth/register'])(
    'redirects an active session before creating %s',
    async (path) => {
      TestBed.inject(TokenStorageService).save(session());
      const initialize = vi.spyOn(RegisterComponent.prototype, 'ngOnInit');
      try {
        const harness = await RouterTestingHarness.create(path);
        expect(TestBed.inject(Router).url).toBe('/library');
        expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain(
          'Tu biblioteca',
        );
        expect(initialize).not.toHaveBeenCalled();
      } finally {
        initialize.mockRestore();
      }
    },
  );

  it('honors a safe returnUrl before loading the auth component', async () => {
    TestBed.inject(TokenStorageService).save(session());
    await RouterTestingHarness.create('/auth/login?returnUrl=%2Flibrary%3Ftab%3Dplaying');
    expect(TestBed.inject(Router).url).toBe('/library?tab=playing');
  });

  it('checks the session again when navigating within the same auth route', async () => {
    const harness = await RouterTestingHarness.create('/auth/login');
    TestBed.inject(TokenStorageService).save(session());
    await harness.navigateByUrl('/auth/login?returnUrl=%2Flibrary');
    expect(TestBed.inject(Router).url).toBe('/library');
  });

  it.each(['/auth/login', '/auth/register'])(
    'allows %s when the stored session has expired',
    async (path) => {
      localStorage.setItem(
        'playtrough.session',
        JSON.stringify({ ...session(), expiresAt: new Date(Date.now() - 1).toISOString() }),
      );
      const harness = await RouterTestingHarness.create(path);
      expect(TestBed.inject(Router).url).toBe(path);
      expect(harness.routeNativeElement?.querySelector('form')).not.toBeNull();
      expect(TestBed.inject(TokenStorageService).read()).toBeNull();
    },
  );

  it.each([
    null,
    '',
    'http://evil.com',
    'javascript:alert(1)',
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
    expect(harness.routeNativeElement?.textContent).toContain('Tu biblioteca');
    await harness.navigateByUrl('/auth/login');
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/library');
  });
});
