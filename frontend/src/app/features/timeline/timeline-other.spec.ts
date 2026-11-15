import { HttpErrorResponse, HttpHeaders, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject, of, tap, throwError } from 'rxjs';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { createFriendsMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { LibraryService } from '../library/services/library';
import { ReviewService } from '../reviews/services/review';
import { UsersService } from '../profile/services/users';
import { TimelinePage } from './models/timeline';
import { TimelineService } from './services/timeline';
import { TimelineComponent } from './timeline';
import { timelineEntry } from './timeline-test-helpers';

describe('Other player timeline', () => {
  function setup() {
    const friends = createFriendsMock();
    const auth = createAuthMock();
    auth.currentUser.set(createSession().user);
    const timeline = {
      getMine: vi.fn(),
      getUser: vi
        .fn()
        .mockReturnValue(of({ items: [timelineEntry()], nextCursor: null, hasMore: false })),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: FriendsService, useValue: friends },
        { provide: AuthService, useValue: auth },
        { provide: TimelineService, useValue: timeline },
        { provide: UsersService, useValue: { getProfile: vi.fn() } },
        { provide: LibraryService, useValue: { changes$: new Subject() } },
        { provide: ReviewService, useValue: { changes$: new Subject() } },
      ],
    });
    const fixture = TestBed.createComponent(TimelineComponent);
    fixture.componentRef.setInput('source', 'bea');
    fixture.componentRef.setInput('readonly', true);
    fixture.componentRef.setInput('relationship', { status: 'none' });
    return { fixture, root: fixture.nativeElement as HTMLElement, friends, timeline };
  }
  const denied = (restriction: string, nested = true) =>
    throwError(
      () =>
        new HttpErrorResponse({
          status: 403,
          error: nested
            ? { extensions: { code: 'history_restricted', restriction } }
            : { code: 'history_restricted', restriction },
        }),
    );
  afterEach(() => vi.useRealTimers());

  it('uses the requested username, third-person text and no owner controls or hidden entries', () => {
    const f = setup();
    f.timeline.getUser.mockReturnValue(
      of({
        items: [timelineEntry(), timelineEntry({ id: 2, isHidden: true })],
        nextCursor: null,
        hasMore: false,
      }),
    );
    f.fixture.detectChanges();
    expect(f.timeline.getMine).not.toHaveBeenCalled();
    expect(f.timeline.getUser).toHaveBeenCalledExactlyOnceWith('bea', { limit: 20 });
    expect(f.root.textContent).toContain('empezó a jugar a');
    expect(f.root.querySelectorAll('app-timeline-entry-card')).toHaveLength(1);
    expect(f.root.querySelector('[aria-haspopup="menu"], [data-hidden]')).toBeNull();
  });
  it.each([
    ['private', true, 'El historial de este jugador es privado'],
    ['friends_only', false, 'El historial es solo para amigos'],
    ['unknown', true, 'El historial de este jugador tiene acceso restringido'],
  ] as const)(
    'renders %s restriction with a lock and friendship action',
    (restriction, nested, message) => {
      const f = setup();
      f.timeline.getUser.mockReturnValue(denied(restriction, nested));
      f.fixture.detectChanges();
      expect(f.root.querySelector('[role="alert"]')?.textContent).toContain(message);
      expect(f.root.querySelector('app-profile-restriction svg')).not.toBeNull();
      expect(f.root.querySelector('app-friendship-button')?.textContent).toContain('Agregar amigo');
      expect(f.root.querySelector('ol')).toBeNull();
      expect(f.root.textContent).not.toContain('Todavía no hay actividad');
    },
  );
  it('reloads after accepting a request, coalescing the service event and button output', async () => {
    const f = setup();
    f.timeline.getUser.mockReturnValueOnce(denied('private'));
    f.fixture.componentRef.setInput('relationship', { status: 'request_received', requestId: 7 });
    f.friends.accept.mockReturnValue(
      of({ friend: { username: 'bea', avatarUrl: null }, since: '' }).pipe(
        tap(() => f.friends.changes.next({ username: 'bea', action: 'accepted' })),
      ),
    );
    f.fixture.detectChanges();
    vi.useFakeTimers();
    [...f.root.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => b.textContent?.includes('Aceptar'))!
      .click();
    await vi.advanceTimersByTimeAsync(0);
    f.fixture.detectChanges();
    expect(f.friends.accept).toHaveBeenCalledExactlyOnceWith(7);
    expect(f.timeline.getUser).toHaveBeenCalledTimes(2);
    expect(f.root.querySelector('ol')).not.toBeNull();
    expect(f.root.querySelector('app-profile-restriction')).toBeNull();
  });
  it('clears visible activity immediately on removal, then reflects the server restriction', async () => {
    const f = setup();
    f.fixture.detectChanges();
    vi.useFakeTimers();
    f.timeline.getUser.mockReturnValueOnce(denied('private'));
    f.friends.changes.next({ username: 'BEA', action: 'removed' });
    f.fixture.detectChanges();
    expect(f.root.querySelector('ol')).toBeNull();
    await vi.advanceTimersByTimeAsync(0);
    f.fixture.detectChanges();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(2);
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('es privado');
  });
  it('ignores unrelated friendships, and defers reloading a hidden tab until shown', async () => {
    const f = setup();
    f.fixture.detectChanges();
    f.friends.changes.next({ username: 'someone_else', action: 'accepted' });
    await f.fixture.whenStable();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(1);
    f.fixture.componentRef.setInput('active', false);
    f.fixture.detectChanges();
    f.friends.changes.next({ username: 'bea', action: 'accepted' });
    await f.fixture.whenStable();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(1);
    f.fixture.componentRef.setInput('active', true);
    f.fixture.detectChanges();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(2);
  });
  it('does not keep old pages after a paginated request is denied', () => {
    const f = setup();
    f.timeline.getUser.mockReturnValueOnce(
      of({ items: [timelineEntry()], nextCursor: 'next', hasMore: true }),
    );
    f.fixture.detectChanges();
    f.timeline.getUser.mockReturnValueOnce(denied('friends_only'));
    f.root.querySelector<HTMLButtonElement>('button:not([aria-haspopup])')!.click();
    f.fixture.detectChanges();
    expect(f.root.querySelector('ol')).toBeNull();
    expect(f.root.textContent).toContain('El historial es solo para amigos');
    expect(f.root.textContent).not.toContain('Cargar más');
  });
  it('shows the same player-not-found message as profiles for user_not_found', () => {
    const f = setup();
    f.timeline.getUser.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 404, error: { code: 'user_not_found' } })),
    );
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain(
      'No encontramos a este jugador',
    );
    expect(f.root.querySelector('a[href="/games"]')).not.toBeNull();
    expect(f.root.querySelector('ol, app-friendship-button')).toBeNull();
  });
  it('shows an other-player empty state without own-library calls to action', () => {
    const f = setup();
    f.timeline.getUser.mockReturnValue(of({ items: [], nextCursor: null, hasMore: false }));
    f.fixture.detectChanges();
    expect(f.root.textContent).toContain('Todavía no hay actividad para mostrar');
    expect(f.root.querySelector('a[href="/library"]')).toBeNull();
  });
  it('shows a recoverable network error and retries the same user', () => {
    const f = setup();
    f.timeline.getUser.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })));
    f.fixture.detectChanges();
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain(
      'No pudimos cargar la actividad',
    );
    f.root.querySelector('button')!.click();
    f.fixture.detectChanges();
    expect(f.timeline.getUser).toHaveBeenLastCalledWith('bea', { limit: 20 });
    expect(f.root.querySelector('ol')).not.toBeNull();
  });
  it('uses the 429 countdown when a friendship changes during the wait', () => {
    vi.useFakeTimers();
    const f = setup();
    f.timeline.getUser.mockReturnValueOnce(
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
    expect(f.root.querySelector('button')!.disabled).toBe(true);
    f.friends.changes.next({ username: 'bea', action: 'accepted' });
    vi.advanceTimersByTime(0);
    f.fixture.detectChanges();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(1);
    expect(f.root.querySelector('[role="alert"]')?.textContent).toContain('2');
    vi.advanceTimersByTime(2000);
    f.fixture.detectChanges();
    f.root.querySelector('button')!.click();
    f.fixture.detectChanges();
    expect(f.timeline.getUser).toHaveBeenCalledTimes(2);
    f.fixture.destroy();
  });
  it('cancels requests on route changes and never mixes different users or old cursors', () => {
    const f = setup();
    const pending = new Subject<TimelinePage>();
    f.timeline.getUser.mockReturnValueOnce(pending);
    f.fixture.detectChanges();
    f.fixture.componentRef.setInput('source', 'carla');
    f.fixture.detectChanges();
    expect(pending.observed).toBe(false);
    pending.next({
      items: [timelineEntry({ game: { ...timelineEntry().game, name: 'STALE' } })],
      nextCursor: 'old',
      hasMore: true,
    });
    f.fixture.detectChanges();
    expect(f.root.textContent).not.toContain('STALE');
    expect(f.timeline.getUser).toHaveBeenLastCalledWith('carla', { limit: 20 });
  });
});
