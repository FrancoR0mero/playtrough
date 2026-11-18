export const FEED_REFRESH_INTERVAL_MS = 30_000;
export const FEED_TEXT = {
  title: 'Inicio',
  list: 'Actividad de tus amigos',
  more: 'Cargar más',
  retry: 'Reintentar',
  news: 'Ver novedades',
  newsAvailable: 'Hay publicaciones nuevas en el inicio.',
  loading: 'Cargando novedades',
  noFriends: 'Todavía no tenés amigos',
  people: 'Buscar personas',
  noActivity: 'Tus amigos todavía no tienen novedades',
  unknownActivity: 'Todavía no hay actividad para mostrar',
  checkingFriends: 'Consultando amistades…',
  retryFriends: 'Reintentar amistades',
  error: 'No pudimos cargar las novedades. Volvé a intentarlo.',
  invalidCursor: 'La página de novedades ya no es válida. Volvé a cargar el inicio.',
  loaded: (count: number) =>
    `${count} ${count === 1 ? 'entrada disponible' : 'entradas disponibles'}`,
  played: 'jugado el',
} as const;
const relativeFormatter = new Intl.RelativeTimeFormat('es-AR', { numeric: 'auto' });
const fullFormatter = new Intl.DateTimeFormat('es-AR', { dateStyle: 'long', timeStyle: 'short' });
const effectiveFormatter = new Intl.DateTimeFormat('es-AR', {
  timeZone: 'UTC',
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
});
// recordedAt is UTC, including provider values serialized without a zone suffix.
export function feedTimestamp(value: string): string {
  return new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(value) ? value : `${value}Z`).toISOString();
}
export function relativeFeedTime(value: string, now: number): string {
  const seconds = (Date.parse(feedTimestamp(value)) - now) / 1000;
  const ranges: readonly [Intl.RelativeTimeFormatUnit, number][] = [
    ['year', 31536000],
    ['month', 2592000],
    ['day', 86400],
    ['hour', 3600],
    ['minute', 60],
  ];
  for (const [unit, size] of ranges)
    if (Math.abs(seconds) >= size)
      return relativeFormatter.format(Math.trunc(seconds / size), unit);
  return relativeFormatter.format(Math.trunc(seconds), 'second');
}
export function fullFeedDate(value: string): string {
  return fullFormatter.format(new Date(feedTimestamp(value)));
}
export function effectiveFeedDate(value: string): string {
  return effectiveFormatter.format(new Date(`${value}T12:00:00Z`));
}
