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
    path: 'games/local/:gameId/reviews',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/reviews/game-reviews').then((m) => m.GameReviewsComponent),
  },
  {
    path: 'games/:externalId/reviews',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/reviews/game-reviews').then((m) => m.GameReviewsComponent),
  },
  {
    path: 'games',
    canActivate: [authGuard],
    loadComponent: () => import('./features/games/games').then((m) => m.GamesComponent),
  },
  {
    path: 'library',
    canActivate: [authGuard],
    loadComponent: () => import('./features/library/library').then((m) => m.LibraryComponent),
  },
  {
    path: 'profile',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile').then((m) => m.ProfileComponent),
  },
  {
    path: 'people',
    canActivate: [authGuard],
    loadComponent: () => import('./features/people/people').then((m) => m.PeopleComponent),
  },
  {
    path: 'friends',
    canActivate: [authGuard],
    loadComponent: () => import('./features/friends/friends').then((m) => m.FriendsComponent),
  },
  {
    path: 'profile/settings/privacy',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/profile/privacy-settings').then((m) => m.PrivacySettingsComponent),
  },
  {
    path: 'profile/settings/blocked',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/blocks/blocked-users').then((m) => m.BlockedUsersComponent),
  },
  {
    path: 'profile/:username/library',
    canActivate: [authGuard],
    loadComponent: () => import('./features/library/library').then((m) => m.LibraryComponent),
  },
  {
    path: 'profile/:username/friends',
    canActivate: [authGuard],
    loadComponent: () => import('./features/friends/friends').then((m) => m.FriendsComponent),
  },
  {
    path: 'profile/:username',
    canActivate: [authGuard],
    loadComponent: () => import('./features/profile/profile').then((m) => m.ProfileComponent),
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
