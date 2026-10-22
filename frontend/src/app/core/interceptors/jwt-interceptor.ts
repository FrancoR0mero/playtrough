import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, of, switchMap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../features/auth/services/auth';
import { TokenStorageService } from '../../features/auth/services/token-storage';
import { PUBLIC_API_ENDPOINTS } from './api-endpoints';
import { RateLimitError } from '../http/rate-limit-error';

export const jwtInterceptor: HttpInterceptorFn = (req, next) => {
  const api = new URL(environment.apiUrl, window.location.origin);
  const url = new URL(req.url, window.location.origin);
  const basePath = api.pathname.replace(/\/$/, '');
  if (
    url.origin !== api.origin ||
    (url.pathname !== basePath && !url.pathname.startsWith(`${basePath}/`))
  )
    return next(req);
  const path = url.pathname.slice(basePath.length).replace(/\/+$/, '').toLowerCase();
  const propagateRateLimit = (error: unknown) =>
    throwError(() =>
      error instanceof HttpErrorResponse &&
      error.status === 429 &&
      !(error instanceof RateLimitError)
        ? new RateLimitError(error)
        : error,
    );
  if (PUBLIC_API_ENDPOINTS.includes(path)) return next(req).pipe(catchError(propagateRateLimit));

  const auth = inject(AuthService);
  const storage = inject(TokenStorageService);
  const token = auth.getAccessToken();
  const withToken = (accessToken: string | null) =>
    accessToken ? req.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } }) : req;

  return next(withToken(token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401)
        return throwError(() => error);
      if (!storage.readTokens()?.refreshToken) {
        auth.expireSession();
        return throwError(() => error);
      }
      // A late 401 may belong to a request sent before another caller renewed the token.
      const currentToken = auth.getAccessToken();
      const renewed = currentToken && currentToken !== token ? of(currentToken) : auth.refresh();
      return renewed.pipe(
        switchMap((newToken) =>
          next(withToken(newToken)).pipe(
            catchError((retryError: unknown) => {
              if (
                retryError instanceof HttpErrorResponse &&
                retryError.status === 401 &&
                auth.getAccessToken() === newToken
              )
                auth.expireSession();
              return throwError(() => retryError);
            }),
          ),
        ),
      );
    }),
    catchError(propagateRateLimit),
  );
};
