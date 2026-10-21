export const APP_ROLES = ['User', 'Moderator', 'Admin'] as const;
export type AppRole = (typeof APP_ROLES)[number];

/** Unknown or missing roles never grant access. */
export function hasMinRole(currentRole: string | null | undefined, minimumRole: string): boolean {
  const current = APP_ROLES.indexOf(currentRole as AppRole);
  const minimum = APP_ROLES.indexOf(minimumRole as AppRole);
  return current >= 0 && minimum >= 0 && current >= minimum;
}
