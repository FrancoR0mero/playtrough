import { filter, fromEvent, interval, merge } from 'rxjs';

/** Recheck server access on return and while visible; no realtime connection. */
export function visibleSocialRefresh() {
  return merge(
    fromEvent(window, 'focus'),
    fromEvent(document, 'visibilitychange'),
    interval(30_000),
  ).pipe(filter(() => document.visibilityState === 'visible'));
}
