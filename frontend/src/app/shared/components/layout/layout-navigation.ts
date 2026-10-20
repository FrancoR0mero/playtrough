import { AppRole } from '../../../core/authorization/roles';

export interface NavigationItem {
  readonly label: string;
  readonly path: string;
  readonly minRole: AppRole;
  readonly enabled: boolean;
}

// Visibility improves UX; the backend is responsible for enforcing permissions.
export const NAVIGATION_ITEMS: readonly NavigationItem[] = [
  { label: 'Biblioteca', path: '/library', minRole: 'User', enabled: true },
  // Enable in phase 4 when /admin/roles is implemented with authGuard and roleGuard.
  { label: 'Roles', path: '/admin/roles', minRole: 'Admin', enabled: false },
  // Enable only when a protected content moderation route and screen exist.
  { label: 'Moderación', path: '/moderation', minRole: 'Moderator', enabled: false },
];
