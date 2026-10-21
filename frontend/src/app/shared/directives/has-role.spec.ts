import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppRole } from '../../core/authorization/roles';
import { createSession } from '../../features/auth/auth-test-helpers';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { HasRoleDirective } from './has-role';

@Component({
  selector: 'app-role-test-host',
  imports: [HasRoleDirective],
  template: '<button *appHasRole="minimum()" type="button">Acción protegida</button>',
})
class RoleTestHost {
  readonly minimum = signal<AppRole>('User');
}

describe('Minimum-role visibility directive', () => {
  let fixture: ComponentFixture<RoleTestHost>;
  let auth: AuthService;
  let storage: TokenStorageService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RoleTestHost],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    auth = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RoleTestHost);
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
    fixture.detectChanges();
  }

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
  ])('renders for %s with minimum %s: %s', (role, minimum, visible) => {
    fixture.componentInstance.minimum.set(minimum);
    restoreRole(role);
    expect(fixture.nativeElement.querySelector('button') !== null).toBe(visible);
  });

  it('does not create protected content without a session', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('adds and removes its view after session and role changes', () => {
    fixture.componentInstance.minimum.set('Moderator');
    restoreRole('User');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    restoreRole('Admin');
    expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
    restoreRole('Moderator');
    expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
    restoreRole('User');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    restoreRole('Admin');
    auth.clearSession();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('reacts when the minimum role input changes', () => {
    restoreRole('User');
    expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
    fixture.componentInstance.minimum.set('Admin');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    fixture.componentInstance.minimum.set('User');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('button')).toHaveLength(1);
  });

  it('does not recreate an already visible view when the user changes', () => {
    restoreRole('User');
    const button = fixture.nativeElement.querySelector('button');
    restoreRole('Admin');
    expect(fixture.nativeElement.querySelector('button')).toBe(button);
  });

  it('does not render content for an unsupported role', () => {
    restoreRole('Owner');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });
});
