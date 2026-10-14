import { Injectable, inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivate,
  Router,
  RouterStateSnapshot,
  UrlTree,
} from '@angular/router';
import { AuthService } from '../../features/auth/services/auth';

@Injectable({ providedIn: 'root' })
export class AuthGuard implements CanActivate {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  canActivate(_route: ActivatedRouteSnapshot, state: RouterStateSnapshot): boolean | UrlTree {
    this.auth.restoreSession();
    return (
      this.auth.isAuthenticated() ||
      this.router.createUrlTree(['/auth/login'], {
        queryParams: { returnUrl: state.url },
      })
    );
  }
}
