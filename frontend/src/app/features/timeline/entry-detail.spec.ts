import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { filter, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { FeedComponent } from '../feed/feed';
import { feedEntry } from '../feed/feed-test-helpers';
import { createFriendsMock, createFriendsStateMock } from '../friends/friends-test-helpers';
import { FriendsService } from '../friends/services/friends';
import { FriendsState } from '../friends/services/friends-state';
import { EntryDetailComponent } from './entry-detail';

const base = `${environment.apiUrl}/timeline`;
const entryUrl = `${base}/entries/42`;

describe('Timeline entry detail', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideLocationMocks(),
        provideRouter([
          { path: 'feed', component: FeedComponent },
          { path: 'entries/:id', component: EntryDetailComponent },
        ]),
        { provide: FriendsService, useValue: createFriendsMock() },
        { provide: FriendsState, useValue: createFriendsStateMock() },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  async function setup(path = '/entries/42') {
    TestBed.inject(Router).setUpLocationChangeListener();
    const harness = await RouterTestingHarness.create(path);
    const http = TestBed.inject(HttpTestingController);
    const root = () => harness.routeNativeElement!;
    const button = (title: string) =>
      [...root().querySelectorAll('button')].find((node) => node.textContent?.trim() === title)!;
    const flushEntry = (likeCount = 0) => {
      http
        .expectOne({ method: 'GET', url: entryUrl })
        .flush(feedEntry({ id: 42, commentCount: 1, likeCount }));
      harness.detectChanges();
      http
        .expectOne(`${entryUrl}/comments?limit=20`)
        .flush({ items: [], commentCount: 0, nextCursor: null, hasMore: false });
      harness.detectChanges();
    };
    return { harness, http, root, button, flushEntry };
  }

  it('protects the route, loads the entry and updates the card from the comment count', async () => {
    expect(routes.find((route) => route.path === 'entries/:id')?.canActivate).toEqual([authGuard]);
    const f = await setup();
    expect(f.root().querySelector('[aria-label="Cargando entrada"]')).not.toBeNull();
    expect(document.activeElement).toBe(f.root().querySelector('h1'));
    f.flushEntry();
    expect(f.root().querySelector('app-timeline-entry-card a[href="/profile/bea"]')).not.toBeNull();
    expect(f.root().querySelector('[data-comment-count]')?.textContent).toContain('0 comentarios');
    f.button('Comentar').click();
    expect(document.activeElement).toBe(f.root().querySelector('textarea'));
    const form = f.root().querySelector('textarea')!;
    form.value = 'un comentario';
    form.dispatchEvent(new Event('input'));
    f.harness.detectChanges();
    f.root()
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    f.http.expectOne({ method: 'POST', url: `${entryUrl}/comments` }).flush(
      {
        comment: {
          id: 1,
          author: { username: 'bea', avatarUrl: null },
          text: 'un comentario',
          createdAt: '2026-03-12T12:00:00Z',
          editedAt: null,
          isMine: true,
          canDelete: true,
          likeCount: 0,
          dislikeCount: 0,
          myReaction: null,
        },
        commentCount: 1,
      },
      { status: 201, statusText: 'Created' },
    );
    f.harness.detectChanges();
    expect(f.root().querySelector('[data-comment-count]')?.textContent).toContain('1 comentario');
  });

  it('shows the unavailable state on 404 and goes to the feed on direct entry', async () => {
    const f = await setup();
    f.http
      .expectOne(entryUrl)
      .flush({ code: 'entry_not_found' }, { status: 404, statusText: 'Not Found' });
    f.harness.detectChanges();
    expect(f.root().querySelector('[role="alert"]')?.textContent).toContain(
      'No encontramos esta entrada o ya no está disponible',
    );
    expect(f.root().querySelector('a[href="/feed"]')).not.toBeNull();
    f.button('Volver').click();
    await f.harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/feed');
    f.http
      .expectOne(`${environment.apiUrl}/feed?limit=20`)
      .flush({ items: [], nextCursor: null, hasMore: false });
  });

  it('lets the user retry a network failure and respects Retry-After', async () => {
    const f = await setup();
    vi.useFakeTimers();
    f.http.expectOne(entryUrl).error(new ProgressEvent('error'));
    f.harness.detectChanges();
    expect(f.root().querySelector('[role="alert"]')?.textContent).toContain(
      'No pudimos cargar la entrada',
    );
    f.button('Reintentar').click();
    f.http
      .expectOne(entryUrl)
      .flush(
        { code: 'rate_limited' },
        { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } },
      );
    f.harness.detectChanges();
    expect(f.root().querySelector('[role="alert"]')?.textContent).toContain('2 segundos');
    expect(f.button('Reintentar').disabled).toBe(true);
    vi.advanceTimersByTime(2000);
    f.harness.detectChanges();
    f.button('Reintentar').click();
    f.flushEntry();
  });

  it('opens the likes list from entry detail', async () => {
    const f = await setup();
    f.flushEntry(2);
    const trigger = f.root().querySelector<HTMLButtonElement>('button[data-view-likes]')!;
    trigger.click();
    TestBed.tick();
    f.http
      .expectOne(`${entryUrl}/likes?limit=20`)
      .flush({ items: [], nextCursor: null, hasMore: false });
    TestBed.tick();
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('Les gustó esto');
    document.querySelector<HTMLButtonElement>('[data-close-likes]')!.click();
    TestBed.tick();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it('opens the composer from a paginated card and restores focus to its link when going back', async () => {
    const f = await setup('/feed');
    const page = (id: number, cursor: string | null = null, next: string | null = null) => {
      f.http
        .expectOne(`${environment.apiUrl}/feed?${cursor ? 'cursor=' + cursor + '&' : ''}limit=20`)
        .flush({ items: [feedEntry({ id })], nextCursor: next, hasMore: !!next });
      f.harness.detectChanges();
    };
    page(5, null, 'v1.more');
    f.button('Cargar más').click();
    page(42, 'v1.more');
    expect(f.root().querySelector('#entry-42-comments')?.getAttribute('href')).toBe('/entries/42');
    const trigger = f.root().querySelector<HTMLAnchorElement>('#entry-42-compose')!;
    trigger.focus();
    trigger.click();
    await f.harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/entries/42?comment=1');
    f.flushEntry();
    expect(document.activeElement).toBe(f.root().querySelector('textarea'));
    const returned = firstValueFrom(
      TestBed.inject(Router).events.pipe(
        filter((event) => event instanceof NavigationEnd && event.urlAfterRedirects === '/feed'),
      ),
    );
    f.button('Volver').click();
    await returned;
    await f.harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/feed');
    page(5, null, 'v1.more');
    page(42, 'v1.more');
    expect(document.activeElement).toBe(f.root().querySelector('#entry-42-compose'));
  });
});
