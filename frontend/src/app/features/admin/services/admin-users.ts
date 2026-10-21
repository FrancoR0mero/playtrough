import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { AppRole } from '../../../core/authorization/roles';
import { AdminUser, PagedResponse, UserListQuery } from '../models/admin-users';

@Injectable({ providedIn: 'root' })
export class AdminUsersService {
  private readonly http = inject(HttpClient);

  listUsers(query: UserListQuery) {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.role) params = params.set('role', query.role);
    return this.http.get<PagedResponse<AdminUser>>(`${environment.apiUrl}/admin/users`, { params });
  }

  changeRole(id: number, role: AppRole) {
    return this.http.put<AdminUser>(`${environment.apiUrl}/admin/users/${id}/role`, { role });
  }
}
