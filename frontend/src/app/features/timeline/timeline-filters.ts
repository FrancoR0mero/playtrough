import { ParamMap } from '@angular/router';
import { TimelineQuery, TimelineType } from './models/timeline';

export type TimelineFilters = Pick<TimelineQuery, 'year' | 'month' | 'type'>;
export const TIMELINE_TYPES: readonly TimelineType[] = ['added', 'status_changed', 'reviewed'];
export function hasTimelineFilters(filters: TimelineFilters): boolean {
  return filters.year !== undefined || filters.month !== undefined || !!filters.type?.length;
}
export function parseTimelineFilters(
  params: ParamMap,
  currentYear = new Date().getFullYear(),
): TimelineFilters | null {
  for (const key of ['year', 'month', 'type']) if (params.getAll(key).length > 1) return null;
  const year = params.get('year'),
    month = params.get('month'),
    type = params.get('type');
  if (year !== null && (!/^\d{4}$/.test(year) || +year < 1970 || +year > currentYear + 1))
    return null;
  if (month !== null && (!/^\d{1,2}$/.test(month) || +month < 1 || +month > 12)) return null;
  const types = type === null ? [] : type.split(',').map((value) => value.trim());
  if (types.some((value) => !TIMELINE_TYPES.includes(value as TimelineType))) return null;
  return {
    ...(year !== null ? { year: +year } : {}),
    ...(month !== null ? { month: +month } : {}),
    ...(types.length ? { type: TIMELINE_TYPES.filter((value) => types.includes(value)) } : {}),
  };
}
