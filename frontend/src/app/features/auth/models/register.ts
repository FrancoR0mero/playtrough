/** Payload for POST /api/auth/register. */
export interface RegisterRequest {
  email: string;
  username: string;
  password: string;
}

/** Public user representation returned after a successful registration (201). */
export interface RegisterResponse {
  id: number;
  username: string;
  email: string;
  createdAt: string;
  emailConfirmationRequired: boolean;
}

/** RFC 9457 validation problem returned with 400; errors are keyed by field name (Email, Username, Password). */
export interface ValidationProblemDetails {
  errors?: Record<string, string[]>;
}

/** RFC 9457 problem details returned with 409 (and other non-validation failures). */
export interface ProblemDetails {
  title?: string;
  detail?: string;
  code?: string;
  extensions?: { code?: string };
}
