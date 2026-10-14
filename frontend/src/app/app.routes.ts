import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'auth/login',
    data: { mode: 'login' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
  {
    path: 'auth/register',
    data: { mode: 'register' },
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
];
