import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'auth/register',
    loadComponent: () => import('./features/auth/register').then((m) => m.RegisterComponent),
  },
];
