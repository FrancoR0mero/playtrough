import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { AdminUsersService } from './admin-users';

describe('AdminUsersService', () => {
  let service: AdminUsersService;
  let httpMock: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminUsersService);
    httpMock = TestBed.inject(HttpTestingController);
  });
  afterEach(() => httpMock.verify());

  it('lists users with pagination, trimmed partial search and role filter', () => {
    const next = vi.fn();
    service
      .listUsers({ page: 2, pageSize: 20, search: '  ana@example.com  ', role: 'Moderator' })
      .subscribe(next);
    const request = httpMock.expectOne((req) => req.url === `${environment.apiUrl}/admin/users`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize', 'role', 'search']);
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('search')).toBe('ana@example.com');
    expect(request.request.params.get('role')).toBe('Moderator');
    const response = { items: [], total: 0, page: 2, pageSize: 20 };
    request.flush(response);
    expect(next).toHaveBeenCalledExactlyOnceWith(response);
  });

  it('omits empty filters instead of sending an invalid empty role', () => {
    service.listUsers({ page: 1, pageSize: 50, search: ' ' }).subscribe();
    const request = httpMock.expectOne((req) => req.url === `${environment.apiUrl}/admin/users`);
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    expect(request.request.params.get('pageSize')).toBe('50');
    request.flush({ items: [], total: 0, page: 1, pageSize: 50 });
  });

  it('changes the role through PUT and returns the updated user', () => {
    const next = vi.fn();
    service.changeRole(7, 'Moderator').subscribe(next);
    const request = httpMock.expectOne(`${environment.apiUrl}/admin/users/7/role`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ role: 'Moderator' });
    const updated = {
      id: 7,
      username: 'ana',
      email: 'ana@example.com',
      role: 'Moderator',
      emailConfirmed: true,
    };
    request.flush(updated);
    expect(next).toHaveBeenCalledExactlyOnceWith(updated);
  });
});
