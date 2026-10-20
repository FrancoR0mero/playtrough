import { signal } from '@angular/core';
import type { Observable } from 'rxjs';
import { vi } from 'vitest';
import { AppRole, hasMinRole } from '../../core/authorization/roles';
import type { AuthUser, LoginResponse } from './models/login';
import type { AuthMessage } from './models/password-recovery';
import type { RegisterRequest, RegisterResponse } from './models/register';

export function createSession(): LoginResponse {
  return {
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  };
}

export function createAuthMock() {
  const currentUser = signal<AuthUser | null>(null);
  const isAuthenticated = signal(false);
  return {
    register: vi.fn<(payload: RegisterRequest) => Observable<RegisterResponse>>(),
    confirmEmail: vi.fn<(email: string, token: string) => Observable<AuthMessage>>(),
    resendConfirmation: vi.fn<(email: string) => Observable<AuthMessage>>(),
    login: vi.fn<(identifier: string, password: string) => Observable<LoginResponse>>(),
    logout: vi.fn<() => Observable<void>>(),
    forgotPassword: vi.fn<(email: string) => Observable<AuthMessage>>(),
    resetPassword:
      vi.fn<
        (
          email: string,
          token: string,
          newPassword: string,
          confirmPassword: string,
        ) => Observable<AuthMessage>
      >(),
    clearSession: vi.fn(),
    restoreSession: vi.fn(),
    currentUser,
    isAuthenticated,
    hasRole: (role: AppRole) => isAuthenticated() && currentUser()?.role === role,
    hasMinRole: (role: AppRole) => isAuthenticated() && hasMinRole(currentUser()?.role, role),
  };
}

export function createTokenStorageMock() {
  return {
    save: vi.fn(),
    read: vi.fn().mockReturnValue(null),
    readTokens: vi.fn().mockReturnValue(null),
    clear: vi.fn(),
  };
}
