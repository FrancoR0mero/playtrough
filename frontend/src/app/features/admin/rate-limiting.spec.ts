import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { TokenStorageService } from '../auth/services/token-storage';
import { AdminRolesComponent } from './roles';

const USERS = [
  { id: 1, username: 'ana_admin', email: 'ana@example.com', role: 'Admin', emailConfirmed: true },
  {
    id: 2,
    username: 'usuario_ejemplo',
    email: 'usuario@example.com',
    role: 'User',
    emailConfirmed: true,
  },
];

describe('Rate limiting on the roles screen', () => {
  let fixture: ComponentFixture<AdminRolesComponent>;
  let httpMock: HttpTestingController;
  let storage: TokenStorageService;
  let auth: AuthService;
  let router: Router;
  const usersUrl = `${environment.apiUrl}/admin/users`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminRolesComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    storage.save({ ...createSession(), user: { ...createSession().user, role: 'Admin' } });
    auth = TestBed.inject(AuthService);
    vi.spyOn(auth, 'refresh');
    vi.spyOn(auth, 'logout');
    vi.spyOn(auth, 'clearSession');
    router = TestBed.inject(Router);
    vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdminRolesComponent);
    fixture.detectChanges();
  });
  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      fixture.destroy();
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
    }
  });

  function assertPreserved(message: string) {
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      `Demasiados intentos. Probá de nuevo en ${message}`,
    );
    expect(auth.isAuthenticated()).toBe(true);
    expect(storage.readTokens()?.refreshToken).toBe('refresh');
    expect(auth.refresh).not.toHaveBeenCalled();
    expect(auth.logout).not.toHaveBeenCalled();
    expect(auth.clearSession).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    httpMock.expectNone(`${environment.apiUrl}/auth/refresh`);
  }

  it.each([
    ['90', '1 minuto y 30 segundos'],
    [undefined, '60 segundos'],
  ])('shows a load 429 with Retry-After=%s and permits an explicit retry', (header, message) => {
    httpMock
      .expectOne((req) => req.url === usersUrl)
      .flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: header ? { 'Retry-After': header } : {},
      });
    fixture.detectChanges();
    assertPreserved(message!);
    httpMock.expectNone((req) => req.url === usersUrl);
    const retry = fixture.nativeElement.querySelector('[role="alert"] button') as HTMLButtonElement;
    expect(retry.disabled).toBe(false);
    retry.click();
    httpMock
      .expectOne((req) => req.url === usersUrl)
      .flush({ items: USERS, total: 2, page: 1, pageSize: 20 });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it.each([
    ['90', '1 minuto y 30 segundos'],
    [undefined, '60 segundos'],
  ])(
    'shows a role-change 429 with Retry-After=%s without changing the role or reloading',
    async (header, message) => {
      httpMock
        .expectOne((req) => req.url === usersUrl)
        .flush({ items: USERS, total: 2, page: 1, pageSize: 20 });
      fixture.detectChanges();
      const select = fixture.nativeElement.querySelector('#user-role-2') as HTMLSelectElement;
      select.value = 'Moderator';
      select.dispatchEvent(new Event('change', { bubbles: true }));
      fixture.detectChanges();
      await fixture.whenStable();
      (document.querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      await fixture.whenStable();
      httpMock.expectOne(`${usersUrl}/2/role`).flush(null, {
        status: 429,
        statusText: 'Too Many Requests',
        headers: header ? { 'Retry-After': header } : {},
      });
      fixture.detectChanges();
      assertPreserved(message!);
      expect(select.value).toBe('User');
      expect(select.disabled).toBe(false);
      httpMock.expectNone((req) => req.url === usersUrl);
    },
  );
});
