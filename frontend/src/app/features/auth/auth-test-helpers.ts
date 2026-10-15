import { signal } from '@angular/core';
import type { Observable } from 'rxjs';
import type { AuthUser, LoginResponse } from './models/login';

export function createSession(): LoginResponse {
  return {
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  };
}

export function createAuthMock() {
  return {
    login: vi.fn<(identifier: string, password: string) => Observable<LoginResponse>>(),
    logout: vi.fn<() => Observable<void>>(),
    restoreSession: vi.fn(),
    currentUser: signal<AuthUser | null>(null),
    isAuthenticated: signal(false),
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
