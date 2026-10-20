import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { authDestination } from '../../features/auth/auth-navigation';
import { AuthService } from '../../features/auth/services/auth';

export const guestGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  auth.restoreSession();
  return (
    !auth.isAuthenticated() ||
    router.parseUrl(authDestination(router, route.queryParamMap.get('returnUrl')))
  );
};
