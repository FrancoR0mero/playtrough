import { HttpErrorResponse } from '@angular/common/http';
import { displayLibraryDate } from './library-date';

interface LibraryProblem {
  code?: string;
  minDate?: string;
  extensions?: { code?: string; minDate?: string };
}

function problem(error: unknown): LibraryProblem | null {
  return error instanceof HttpErrorResponse ? (error.error as LibraryProblem | null) : null;
}

export function libraryErrorCode(error: unknown): string | undefined {
  const value = problem(error);
  return value?.extensions?.code ?? value?.code;
}

export function libraryErrorMessage(error: unknown, mutation = true): string {
  const value = problem(error);
  const minDate = value?.extensions?.minDate ?? value?.minDate;
  switch (libraryErrorCode(error)) {
    case 'already_in_library':
      return 'Este juego ya está en tu biblioteca. Actualizamos su estado.';
    case 'not_in_library':
      return 'Este juego ya no está en tu biblioteca.';
    case 'invalid_status':
      return 'Elegí un estado válido.';
    case 'invalid_date':
      return 'Revisá la fecha';
    case 'date_before_previous_event':
      return `La fecha no puede ser anterior al último cambio${minDate ? ` (${displayLibraryDate(minDate)})` : ''}`;
    case 'game_not_found':
      return 'No encontramos este juego.';
    case 'concurrent_modification':
      return 'Tu biblioteca cambió en otra sesión. Revisá el estado actualizado y volvé a intentar.';
    case 'invalid_query':
      return 'La búsqueda no es válida. Revisá el texto e intentá de nuevo.';
    default:
      return `No pudimos ${mutation ? 'actualizar' : 'cargar'} tu biblioteca. Revisá tu conexión e intentá de nuevo.`;
  }
}
