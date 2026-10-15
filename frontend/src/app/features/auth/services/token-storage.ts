import { Injectable } from '@angular/core';
import type { LoginResponse } from '../models/login';

// Decisión de seguridad a justificar en la tesis: localStorage permite persistir la sesión,
// pero sus tokens son accesibles desde JavaScript ante XSS. Este servicio permite cambiar
// el mecanismo de almacenamiento sin modificar a sus consumidores.
const SESSION_KEY = 'playtrough.session';

@Injectable({ providedIn: 'root' })
export class TokenStorageService {
  save(session: LoginResponse): void {
    if (!this.isValidSession(session)) throw new Error('Invalid or expired session');
    localStorage.setItem(SESSION_KEY, JSON.stringify(session));
  }

  read(): LoginResponse | null {
    try {
      const raw = localStorage.getItem(SESSION_KEY);
      if (raw === null) return null;
      const session: unknown = JSON.parse(raw);
      if (this.isValidSession(session)) return session;
    } catch {
      // Missing browser storage, malformed JSON or denied access means no restorable session.
    }
    this.clear();
    return null;
  }

  /** Logout may send an expired access token; this never restores an expired session. */
  readTokens(): Pick<LoginResponse, 'accessToken' | 'refreshToken'> | null {
    try {
      const raw = localStorage.getItem(SESSION_KEY);
      if (raw === null) return null;
      const session: unknown = JSON.parse(raw);
      if (typeof session !== 'object' || session === null) return null;
      const { accessToken, refreshToken } = session as Partial<LoginResponse>;
      if (
        typeof accessToken === 'string' &&
        accessToken.trim().length > 0 &&
        typeof refreshToken === 'string' &&
        refreshToken.trim().length > 0
      ) {
        return { accessToken, refreshToken };
      }
    } catch {
      // Missing or unavailable storage means there are no tokens to revoke remotely.
    }
    return null;
  }

  clear(): void {
    try {
      localStorage.removeItem(SESSION_KEY);
    } catch {
      // Logout must still clear the in-memory state when storage is unavailable.
    }
  }

  private isValidSession(value: unknown): value is LoginResponse {
    if (typeof value !== 'object' || value === null) return false;
    const session = value as Partial<LoginResponse>;
    const user = session.user;
    return (
      typeof session.accessToken === 'string' &&
      session.accessToken.length > 0 &&
      typeof session.refreshToken === 'string' &&
      session.refreshToken.length > 0 &&
      typeof session.expiresAt === 'string' &&
      Date.parse(session.expiresAt) > Date.now() &&
      typeof user === 'object' &&
      user !== null &&
      Number.isInteger(user.id) &&
      user.id > 0 &&
      typeof user.username === 'string' &&
      typeof user.email === 'string' &&
      typeof user.role === 'string'
    );
  }
}
