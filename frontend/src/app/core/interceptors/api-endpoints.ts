/** Paths relative to environment.apiUrl. These requests never use bearer authentication. */
export const PUBLIC_API_ENDPOINTS: readonly string[] = [
  '/auth/login',
  '/auth/register',
  '/auth/refresh',
  '/auth/forgot-password',
  '/auth/reset-password',
  '/auth/confirm-email',
  '/auth/resend-confirmation',
];
