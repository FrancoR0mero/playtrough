import { TimelineEntryDto, TimelinePerspective } from './models/timeline';

export const TIMELINE_IMAGE_ASPECT_RATIO = '16 / 9';
export const TIMELINE_TEXT = {
  profile: 'Perfil',
  timeline: 'Línea de tiempo',
  ownSections: 'Secciones de mi perfil',
  otherSections: 'Secciones del perfil',
  privateHistory: 'El historial de este jugador es privado',
  friendsHistory: 'El historial es solo para amigos',
  restrictedHistory: 'El historial de este jugador tiene acceso restringido',
  notFound: 'No encontramos a este jugador',
  list: 'Actividad de juegos',
  empty: 'Tu línea de tiempo está vacía',
  emptyOther: 'Todavía no hay actividad para mostrar',
  games: 'Explorar juegos',
  library: 'Ver mi biblioteca',
  more: 'Cargar más',
  loading: 'Cargando actividad',
  retry: 'Reintentar',
  hidden: 'Oculta para otros',
  reviews: 'Ver reseñas',
  error: 'No pudimos cargar la actividad. Volvé a intentarlo.',
  invalidCursor: 'La página de actividad ya no es válida. Volvé a cargar la línea de tiempo.',
  loaded: (count: number) =>
    `${count} ${count === 1 ? 'entrada disponible' : 'entradas disponibles'}`,
  actions: {
    started: ['Empezaste a jugar a', 'empezó a jugar a'],
    addedBacklog: ['Agregaste al backlog', 'agregó al backlog'],
    movedBacklog: ['Mandaste al backlog', 'mandó al backlog'],
    resumed: ['Retomaste', 'retomó'],
    beaten: ['Terminaste', 'terminó'],
    completed: ['Completaste al 100 %', 'completó al 100 %'],
    abandoned: ['Abandonaste', 'abandonó'],
    reviewed: ['Reseñaste', 'reseñó'],
    added: ['Agregaste a tu biblioteca', 'agregó a su biblioteca'],
    changed: ['Cambiaste el estado de', 'cambió el estado de'],
  },
} as const;

export function entryAction(entry: TimelineEntryDto, perspective: TimelinePerspective): string {
  let action: keyof typeof TIMELINE_TEXT.actions;
  if (entry.type === 'reviewed') action = 'reviewed';
  else
    switch (entry.toStatus) {
      case 'playing':
        action =
          entry.type === 'status_changed' &&
          ['beaten', 'completed', 'abandoned'].includes(entry.fromStatus ?? '')
            ? 'resumed'
            : 'started';
        break;
      case 'backlog':
        action = entry.type === 'added' ? 'addedBacklog' : 'movedBacklog';
        break;
      case 'beaten':
        action = 'beaten';
        break;
      case 'completed':
        action = 'completed';
        break;
      case 'abandoned':
        action = 'abandoned';
        break;
      default:
        action = entry.type === 'added' ? 'added' : 'changed';
    }
  return TIMELINE_TEXT.actions[action][perspective === 'self' ? 0 : 1];
}
export function describeEntry(entry: TimelineEntryDto, perspective: TimelinePerspective): string {
  return `${entryAction(entry, perspective)} ${entry.game.name}`;
}

// A DateOnly is a calendar date, never an instant in the viewer's timezone.
export function timelineDate(value: string, monthOnly = false): string {
  const label = new Intl.DateTimeFormat('es-AR', {
    ...(monthOnly ? {} : { day: 'numeric' as const }),
    month: 'long',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(`${value.slice(0, 10)}T12:00:00Z`));
  return monthOnly ? label[0].toUpperCase() + label.slice(1) : label;
}
