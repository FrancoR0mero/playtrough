import { HttpErrorResponse } from '@angular/common/http';

export function profileError(error: unknown): { code?: string; availableAt?: string } {
  if (!(error instanceof HttpErrorResponse) || !error.error || typeof error.error !== 'object')
    return {};
  const body = error.error;
  const code = body.extensions?.code ?? body.code;
  const availableAt = body.extensions?.availableAt ?? body.availableAt;
  return {
    code: typeof code === 'string' ? code : undefined,
    availableAt:
      typeof availableAt === 'string' && Number.isFinite(Date.parse(availableAt))
        ? availableAt
        : undefined,
  };
}

export function profileDate(value: string): string {
  return new Intl.DateTimeFormat('es-AR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(new Date(value));
}
