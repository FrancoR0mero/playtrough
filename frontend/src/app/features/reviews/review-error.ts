import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../auth/models/register';

export function reviewErrorCode(error: unknown): string | undefined {
  const problem =
    error instanceof HttpErrorResponse ? (error.error as ProblemDetails | null) : null;
  return problem?.extensions?.code ?? problem?.code;
}

export function reviewErrorMessage(error: unknown): string {
  switch (reviewErrorCode(error)) {
    case 'invalid_rating':
      return 'Elegí una puntuación';
    case 'invalid_text':
      return 'El texto debe tener hasta 2.000 caracteres.';
    case 'game_not_found':
      return 'No encontramos este juego';
    case 'concurrent_modification':
      return 'Tu reseña cambió en otra sesión. Podés reintentar para guardar lo que escribiste.';
    default:
      return 'No pudimos completar la operación. Revisá tu conexión e intentá de nuevo.';
  }
}
