import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../../features/auth/services/auth';
import { hasMinRole } from '../authorization/roles';
import { requireSession } from './session-access';

export const roleGuard: CanActivateFn = (route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const minRole: unknown = route.data['minRole'];
  return requireSession(state.url).pipe(
    map((result) => {
      if (result !== true) return result;
      return typeof minRole === 'string' && hasMinRole(auth.currentUser()?.role, minRole)
        ? true
        : router.parseUrl('/access-denied');
    }),
  );
};
