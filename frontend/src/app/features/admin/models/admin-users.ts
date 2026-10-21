import { AppRole } from '../../../core/authorization/roles';

export interface AdminUser {
  id: number;
  username: string;
  email: string;
  role: AppRole;
  emailConfirmed: boolean;
}

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UserListQuery {
  page: number;
  pageSize: number;
  search?: string;
  role?: AppRole;
}

export interface RoleChangeConfirmation {
  user: AdminUser;
  role: AppRole;
}
