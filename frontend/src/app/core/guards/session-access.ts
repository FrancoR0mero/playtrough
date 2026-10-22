import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router, UrlTree } from '@angular/router';
import { Observable, catchError, map, of } from 'rxjs';
import { authDestination } from '../../features/auth/auth-navigation';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { RateLimitError } from '../http/rate-limit-error';
import { RateLimitNoticeService } from '../../shared/services/rate-limit-notice';

/** Shared session check so roleGuard also works when used without authGuard. */
export function requireSession(url: string): Observable<boolean | UrlTree> {
  const auth = inject(AuthService);
  const storage = inject(TokenStorageService);
  const router = inject(Router);
  const rateLimitNotice = inject(RateLimitNoticeService);
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
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 429) {
        const limited = error instanceof RateLimitError ? error : new RateLimitError(error);
        rateLimitNotice.show(limited.retryAfterSeconds);
        return of(false);
      }
      return of(
        login(error instanceof HttpErrorResponse && (error.status === 400 || error.status === 401)),
      );
    }),
  );
}
