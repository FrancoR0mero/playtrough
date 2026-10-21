import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth-guard';
import { guestGuard } from './core/guards/guest-guard';
import { roleGuard } from './core/guards/role-guard';

export const routes: Routes = [
  {
    path: 'confirm-email',
    loadComponent: () =>
      import('./features/auth/confirm-email').then((m) => m.ConfirmEmailComponent),
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password').then((m) => m.ResetPasswordComponent),
  },
  {
    path: 'auth/forgot-password',
    canActivate: [guestGuard],
    loadComponent: () =>
      import('./features/auth/forgot-password').then((m) => m.ForgotPasswordComponent),
  },
  { path: '', pathMatch: 'full', redirectTo: 'library' },
  {
    path: 'auth/login',
    canActivate: [guestGuard],
    runGuardsAndResolvers: 'always',
    data: { mode: 'login' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'auth/register',
    canActivate: [guestGuard],
    runGuardsAndResolvers: 'always',
    data: { mode: 'register' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'library',
    canActivate: [authGuard],
    loadComponent: () => import('./features/library/library').then((m) => m.LibraryComponent),
  },
  {
    path: 'admin/roles',
    canActivate: [authGuard, roleGuard],
    data: { minRole: 'Admin' },
    loadComponent: () => import('./features/admin/roles').then((m) => m.AdminRolesComponent),
  },
  {
    path: 'access-denied',
    loadComponent: () =>
      import('./shared/components/access-denied/access-denied').then(
        (m) => m.AccessDeniedComponent,
      ),
  },
  {
    path: '**',
    loadComponent: () =>
      import('./shared/components/not-found/not-found').then((m) => m.NotFoundComponent),
  },
];
