import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { computed } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { APP_ROLES, AppRole } from '../../../core/authorization/roles';
import { createSession } from '../auth-test-helpers';
import { AuthService } from './auth';
import { TokenStorageService } from './token-storage';

describe('AuthService reactive role queries', () => {
  let auth: AuthService;
  let storage: TokenStorageService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
    }
  });

  function restoreRole(role: string): void {
    storage.save({ ...createSession(), user: { ...createSession().user, role } });
    auth.restoreSession();
  }

  it.each(APP_ROLES)('hasRole matches only the exact role for %s', (role) => {
    restoreRole(role);
    APP_ROLES.forEach((candidate) => expect(auth.hasRole(candidate)).toBe(candidate === role));
  });

  it.each<[AppRole, AppRole, boolean]>([
    ['User', 'User', true],
    ['User', 'Moderator', false],
    ['User', 'Admin', false],
    ['Moderator', 'User', true],
    ['Moderator', 'Moderator', true],
    ['Moderator', 'Admin', false],
    ['Admin', 'User', true],
    ['Admin', 'Moderator', true],
    ['Admin', 'Admin', true],
  ])('hasMinRole checks %s against %s: %s', (role, minimum, allowed) => {
    restoreRole(role);
    expect(auth.hasMinRole(minimum)).toBe(allowed);
  });

  it.each(APP_ROLES)('both queries return false without a session for %s', (role) => {
    expect(auth.hasRole(role)).toBe(false);
    expect(auth.hasMinRole(role)).toBe(false);
  });

  it('reacts to login, refreshed roles and logout without recreating computed consumers', () => {
    const isAdmin = computed(() => auth.hasRole('Admin'));
    const canModerate = computed(() => auth.hasMinRole('Moderator'));
    expect(isAdmin()).toBe(false);
    expect(canModerate()).toBe(false);

    auth.login('ana', 'password').subscribe();
    httpMock.expectOne(`${environment.apiUrl}/auth/login`).flush({
      ...createSession(),
      user: { ...createSession().user, role: 'Moderator' },
    });
    expect(isAdmin()).toBe(false);
    expect(canModerate()).toBe(true);

    auth.refresh().subscribe();
    httpMock.expectOne(`${environment.apiUrl}/auth/refresh`).flush({
      ...createSession(),
      accessToken: 'new-access',
      refreshToken: 'new-refresh',
      user: { ...createSession().user, role: 'Admin' },
    });
    expect(isAdmin()).toBe(true);
    expect(canModerate()).toBe(true);

    auth.logout().subscribe();
    httpMock
      .expectOne(`${environment.apiUrl}/auth/logout`)
      .flush(null, { status: 204, statusText: 'No Content' });
    expect(isAdmin()).toBe(false);
    expect(canModerate()).toBe(false);
  });

  it('reacts to a demotion', () => {
    const canModerate = computed(() => auth.hasMinRole('Moderator'));
    restoreRole('Admin');
    expect(canModerate()).toBe(true);
    restoreRole('User');
    expect(canModerate()).toBe(false);
  });

  it('does not grant supported roles to an unknown role', () => {
    restoreRole('Owner');
    APP_ROLES.forEach((role) => {
      expect(auth.hasRole(role)).toBe(false);
      expect(auth.hasMinRole(role)).toBe(false);
    });
  });

  it('does not grant access for an expired stored session', () => {
    localStorage.setItem(
      'playtrough.session',
      JSON.stringify({
        ...createSession(),
        expiresAt: new Date(Date.now() - 1000).toISOString(),
        user: { ...createSession().user, role: 'Admin' },
      }),
    );
    auth.restoreSession();
    APP_ROLES.forEach((role) => {
      expect(auth.hasRole(role)).toBe(false);
      expect(auth.hasMinRole(role)).toBe(false);
    });
  });
});
