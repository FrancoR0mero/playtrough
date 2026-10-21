import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { authDestination } from '../../features/auth/auth-navigation';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';

/** Shared session check so roleGuard also works when used without authGuard. */
export function requireSession(url: string): Observable<true | UrlTree> {
  const auth = inject(AuthService);
  const storage = inject(TokenStorageService);
  const router = inject(Router);
  const login = (expired = false) =>
    router.createUrlTree(['/auth/login'], {
      queryParams: {
        returnUrl: authDestination(router, url),
        ...(expired ? { sessionExpired: true } : {}),
      },
    });
  auth.restoreSession();
  if (auth.isAuthenticated() && !auth.isAccessTokenExpired(0)) return of(true);
  if (!storage.readTokens()?.refreshToken) return of(login());
  return auth.refresh({ redirectOnFailure: false }).pipe(
    map(() =>
      auth.isAuthenticated() && !auth.isAccessTokenExpired(0) ? (true as const) : login(),
    ),
    catchError((error: unknown) =>
      of(
        login(error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401)),
      ),
    ),
  );
}
