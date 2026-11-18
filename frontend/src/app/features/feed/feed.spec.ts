import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createFriendsMock, createFriendsStateMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { FriendsState } from '../friends/services/friends-state';
import { FeedEntryDto, TimelinePage } from '../timeline/models/timeline';
import { TimelineService } from '../timeline/services/timeline';
import { FeedComponent } from './feed';
import { feedEntry } from './feed-test-helpers';

describe('Friends feed', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-03-12T14:00:00Z'));
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });
  function setup(
    page: TimelinePage<FeedEntryDto> = { items: [feedEntry()], nextCursor: null, hasMore: false },
  ) {
    const service = { getFeed: vi.fn().mockReturnValue(of(page)) },
      friends = createFriendsMock(),
      state = createFriendsStateMock();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: TimelineService, useValue: service },
        { provide: FriendsService, useValue: friends },
        { provide: FriendsState, useValue: state },
      ],
    });
    const fixture = TestBed.createComponent(FeedComponent);
    const root = fixture.nativeElement as HTMLElement;
    const button = (name: string) => root.querySelector<HTMLButtonElement>(`[data-feed-${name}]`)!;
    const focus = () => {
      document.dispatchEvent(new Event('visibilitychange'));
      fixture.detectChanges();
    };
    const wait = (ms = 30_000) => {
      vi.advanceTimersByTime(ms);
      fixture.detectChanges();
    };
    return { fixture, root, service, friends, state, button, focus, wait };
  }
  it('renders the reusable cards with authors and announces loaded results', () => {
    const f = setup();
    f.fixture.detectChanges();
    expect(f.service.getFeed).toHaveBeenCalledExactlyOnceWith({ limit: 20 });
    expect(f.root.querySelector('ol')?.getAttribute('aria-label')).toBe('Actividad de tus amigos');
    expect(f.root.querySelector('a[href="/profile/bea"]')).not.toBeNull();
    expect(f.root.querySelector('[aria-live="polite"]')?.textContent).toContain(
      '1 entrada disponible',
    );
    expect(f.root.querySelector('[data-feed-more]')).toBeNull();
  });
  it('shows skeletons and paginates without duplicate entries or requests', () => {
    const f = setup({ items: [feedEntry()], nextCursor: 'v1.opaque', hasMore: true });
    const first = new Subject<TimelinePage<FeedEntryDto>>();
    f.service.getFeed.mockReturnValueOnce(first);
    f.fixture.detectChanges();
    expect(f.root.querySelector('[data-feed-skeleton]')).not.toBeNull();
    first.next({ items: [feedEntry()], nextCursor: 'v1.opaque', hasMore: true });
    f.fixture.detectChanges();
    const next = new Subject<TimelinePage<FeedEntryDto>>();
    f.service.getFeed.mockReturnValueOnce(next);
    f.button('more').click();
    f.button('more').click();
    f.fixture.detectChanges();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    expect(f.service.getFeed).toHaveBeenLastCalledWith({ limit: 20, cursor: 'v1.opaque' });
    next.next({ items: [feedEntry(), feedEntry({ id: 2 })], nextCursor: null, hasMore: false });
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(2);
    expect(f.root.querySelector('[data-feed-more]')).toBeNull();
  });
  it.each([
    [0, 'Todavía no tenés amigos'],
    [2, 'Tus amigos todavía no tienen novedades'],
    [null, 'Todavía no hay actividad para mostrar'],
  ] as const)('distinguishes empty feed with friends count %s', (count, text) => {
    const f = setup({ items: [], nextCursor: null, hasMore: false });
    f.state.summary.set(
      count === null
        ? null!
        : { friendsCount: count, pendingReceivedCount: 0, pendingSentCount: 0 },
    );
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain(text);
    expect(!!f.root.querySelector('a[href="/people"]')).toBe(count === 0);
  });
  it('handles a summary failure without claiming that the user has no friends', () => {
    const f = setup({ items: [], nextCursor: null, hasMore: false });
    f.state.summary.set(null!);
    f.state.error.set('Error de amistades');
    f.fixture.detectChanges();
    expect(f.root.textContent).not.toContain('Todavía no tenés amigos');
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('Error de amistades');
    [...f.root.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('Reintentar amistades'))!
      .click();
    expect(f.state.refresh).toHaveBeenCalledOnce();
  });
  it('checks every 30 seconds while visible and does not duplicate a check on focus', () => {
    const f = setup();
    f.fixture.detectChanges();
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(1);
    f.wait(29_000);
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(1);
    f.wait(1000);
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    window.dispatchEvent(new Event('focus'));
    f.fixture.detectChanges();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    f.wait();
    expect(f.service.getFeed).toHaveBeenCalledTimes(3);
    expect(f.service.getFeed).toHaveBeenLastCalledWith({ limit: 20 });
    expect(f.root.querySelector('[data-feed-refresh]')).toBeNull();
    expect(f.root.textContent).not.toContain('Disponible en');
  });
  it('pauses checks while hidden and checks on return', () => {
    const f = setup();
    f.fixture.detectChanges();
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    f.wait(120_000);
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(1);
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
  });
  it('keeps the list and focus unchanged until See accepts a new first page', () => {
    const f = setup({ items: [feedEntry()], nextCursor: 'old', hasMore: true });
    f.fixture.detectChanges();
    const game = f.root.querySelector<HTMLButtonElement>('[aria-haspopup="dialog"]')!;
    game.focus();
    f.service.getFeed.mockReturnValueOnce(
      of({
        items: [feedEntry({ id: 3, game: { ...feedEntry().game, name: 'NEW GAME' } })],
        nextCursor: 'fresh',
        hasMore: true,
      }),
    );
    f.wait();
    f.focus();
    expect(f.button('see').textContent).toContain('Ver novedades');
    expect(f.root.querySelector('ol')?.textContent).not.toContain('NEW GAME');
    expect(document.activeElement).toBe(game);
    f.button('see').click();
    f.fixture.detectChanges();
    expect(f.root.querySelector('ol')?.textContent).toContain('NEW GAME');
    expect(f.root.querySelector('[data-feed-see]')).toBeNull();
    expect(document.activeElement).toBe(f.root.querySelector('h1'));
    f.button('more').click();
    expect(f.service.getFeed).toHaveBeenLastCalledWith({ limit: 20, cursor: 'fresh' });
  });
  it('does not announce news when the first entry is unchanged, older or removed', () => {
    const f = setup();
    f.fixture.detectChanges();
    f.wait();
    f.focus();
    expect(f.root.querySelector('[data-feed-see]')).toBeNull();
    for (const items of [[feedEntry({ id: 2, recordedAt: '2026-03-12T11:00:00Z' })], []]) {
      f.service.getFeed.mockReturnValueOnce(of({ items, nextCursor: null, hasMore: false }));
      f.wait();
      expect(f.root.querySelector('[data-feed-see]')).toBeNull();
    }
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(1);
  });
  it('announces the first new entry when the feed was empty', () => {
    const f = setup({ items: [], nextCursor: null, hasMore: false });
    f.fixture.detectChanges();
    f.service.getFeed.mockReturnValueOnce(
      of({ items: [feedEntry()], nextCursor: null, hasMore: false }),
    );
    f.wait();
    expect(f.root.querySelector('ol')).toBeNull();
    f.button('see').click();
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(1);
  });
  it.each([0, 503, 429])('silently ignores background errors with status %s', (status) => {
    const f = setup();
    f.fixture.detectChanges();
    const raw = new HttpErrorResponse({
      status,
      headers: new HttpHeaders({ 'Retry-After': '60' }),
    });
    f.service.getFeed.mockReturnValueOnce(
      throwError(() => (status === 429 ? new RateLimitError(raw) : raw)),
    );
    f.wait();
    f.focus();
    expect(f.root.querySelector('[role="alert"]')).toBeNull();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(1);
    if (status === 429) {
      f.wait(29_000);
      f.focus();
      expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    }
  });
  it('retries network errors and the same failed pagination cursor', () => {
    const f = setup({ items: [feedEntry()], nextCursor: 'next', hasMore: true });
    f.service.getFeed.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.fixture.detectChanges();
    f.button('retry').click();
    f.fixture.detectChanges();
    f.service.getFeed.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    f.button('more').click();
    f.fixture.detectChanges();
    expect(f.root.querySelectorAll('ol > li')).toHaveLength(1);
    f.button('retry').click();
    f.fixture.detectChanges();
    expect(f.service.getFeed).toHaveBeenLastCalledWith({ limit: 20, cursor: 'next' });
  });
  it('honors foreground 429 and restarts invalid cursors from the first page', () => {
    const f = setup({ items: [feedEntry()], nextCursor: 'old', hasMore: true });
    f.service.getFeed.mockReturnValueOnce(
      throwError(
        () =>
          new RateLimitError(
            new HttpErrorResponse({
              status: 429,
              headers: new HttpHeaders({ 'Retry-After': '2' }),
            }),
          ),
      ),
    );
    f.fixture.detectChanges();
    expect(f.button('retry').disabled).toBe(true);
    f.wait(2000);
    f.button('retry').click();
    f.fixture.detectChanges();
    f.service.getFeed.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 400, error: { code: 'invalid_cursor' } })),
    );
    f.button('more').click();
    f.fixture.detectChanges();
    f.button('retry').click();
    expect(f.service.getFeed).toHaveBeenLastCalledWith({ limit: 20 });
  });
  it('reloads friendship changes immediately when visible and on return when hidden', () => {
    const f = setup();
    f.fixture.detectChanges();
    f.friends.changes.next({ username: 'bea', action: 'removed' });
    f.fixture.detectChanges();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    f.friends.changes.next({ username: 'bea', action: 'accepted' });
    f.fixture.detectChanges();
    expect(f.root.querySelector('ol')).toBeNull();
    expect(f.service.getFeed).toHaveBeenCalledTimes(2);
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    f.focus();
    expect(f.service.getFeed).toHaveBeenCalledTimes(3);
  });
  it('cancels pending reads and removes timers and focus listeners when destroyed', () => {
    const f = setup();
    const pending = new Subject<TimelinePage<FeedEntryDto>>();
    f.service.getFeed.mockReturnValueOnce(pending);
    f.fixture.detectChanges();
    const timers = vi.getTimerCount();
    f.fixture.destroy();
    expect(pending.observed).toBe(false);
    expect(vi.getTimerCount()).toBeLessThan(timers);
    f.wait();
    document.dispatchEvent(new Event('visibilitychange'));
    expect(f.service.getFeed).toHaveBeenCalledTimes(1);
  });
});
