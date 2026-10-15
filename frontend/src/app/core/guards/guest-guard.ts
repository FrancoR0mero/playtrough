import { Injectable, inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router, UrlTree } from '@angular/router';
import { authDestination } from '../../features/auth/auth-navigation';
import { AuthService } from '../../features/auth/services/auth';

@Injectable({ providedIn: 'root' })
export class GuestGuard implements CanActivate {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  canActivate(route: ActivatedRouteSnapshot): boolean | UrlTree {
    this.auth.restoreSession();
    if (!this.auth.isAuthenticated()) return true;

    return this.router.parseUrl(authDestination(this.router, route.queryParamMap.get('returnUrl')));
  }
}
