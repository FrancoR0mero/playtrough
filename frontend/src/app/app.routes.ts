import { Routes } from '@angular/router';
import { AuthGuard } from './core/guards/auth-guard';
import { GuestGuard } from './core/guards/guest-guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'library' },
  {
    path: 'auth/login',
    canActivate: [GuestGuard],
    runGuardsAndResolvers: 'always',
    data: { mode: 'login' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'auth/register',
    canActivate: [GuestGuard],
    runGuardsAndResolvers: 'always',
    data: { mode: 'register' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'library',
    canActivate: [AuthGuard],
    loadComponent: () => import('./features/library/library').then((m) => m.LibraryComponent),
  },
  { path: '**', redirectTo: 'library' },
];
