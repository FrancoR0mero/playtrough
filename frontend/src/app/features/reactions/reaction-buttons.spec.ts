import { HttpHeaders, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { createAuthMock, createSession } from '../auth/auth-test-helpers';
import { AuthService } from '../auth/services/auth';
import { ReactionSummaryDto, ReactionTarget, ReactionValue } from './models/reaction';
import { ReactionButtonsComponent } from './reaction-buttons';
import { ReactionsService } from './services/reactions';

const entry: ReactionTarget = { type: 'entry', id: 42 };
const url = `${environment.apiUrl}/timeline/entries/42/reaction`;
const summary = (
  myReaction: ReactionValue | null = null,
  likeCount = 10,
  dislikeCount = 5,
): ReactionSummaryDto => ({ myReaction, likeCount, dislikeCount });

describe('ReactionButtonsComponent', () => {
  const auth = createAuthMock();

  beforeEach(() => {
    auth.currentUser.set(createSession().user);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  function mount(initial = summary(), isOwn = false, target = entry) {
    const fixture = TestBed.createComponent(ReactionButtonsComponent);
    fixture.componentRef.setInput('target', target);
    fixture.componentRef.setInput('summary', initial);
    fixture.componentRef.setInput('isOwn', isOwn);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const button = (value: ReactionValue) =>
      root.querySelector<HTMLButtonElement>(`[data-reaction="${value}"]`)!;
    return {
      fixture,
      root,
      button,
      click(value: ReactionValue) {
        button(value).click();
        fixture.detectChanges();
      },
    };
  }

  function expectState(view: ReturnType<typeof mount>, state: ReactionSummaryDto) {
    view.fixture.detectChanges();
    expect(view.button('like').getAttribute('aria-pressed')).toBe(
      String(state.myReaction === 'like'),
    );
    expect(view.button('dislike').getAttribute('aria-pressed')).toBe(
      String(state.myReaction === 'dislike'),
    );
    expect(view.button('like').getAttribute('aria-label')).toBe(`Me gusta (${state.likeCount})`);
    expect(view.button('dislike').getAttribute('aria-label')).toBe(
      `No me gusta (${state.dislikeCount})`,
    );
  }

  it.each([
    [null, 'like', summary('like', 11, 5)],
    [null, 'dislike', summary('dislike', 10, 6)],
    ['like', 'dislike', summary('dislike', 9, 6)],
    ['dislike', 'like', summary('like', 11, 4)],
    ['like', 'like', summary(null, 9, 5)],
    ['dislike', 'dislike', summary(null, 10, 4)],
  ] as const)(
    'projects %s after pressing %s and reconciles counts',
    (previous, value, optimistic) => {
      const view = mount(summary(previous));
      view.click(value);
      expectState(view, optimistic);
      const method = optimistic.myReaction === null ? 'DELETE' : 'PUT';
      const request = TestBed.inject(HttpTestingController).expectOne({ method, url });
      const server = summary(optimistic.myReaction, 17, 9);
      request.flush(server);
      expectState(view, server);
      expect(view.root.querySelector('[aria-live="polite"]')?.textContent).toContain(
        server.myReaction === null
          ? 'Quitaste tu reacción'
          : server.myReaction === 'like'
            ? 'Te gusta esta entrada'
            : 'No te gusta esta entrada',
      );
    },
  );

  it('shares optimistic and confirmed state across cards and sends only the last queued state', () => {
    vi.useFakeTimers();
    const view = mount();
    const other = mount(summary(), false, { type: 'entry', id: 43 });
    const http = TestBed.inject(HttpTestingController);
    const changes = vi.fn();
    const subscription = TestBed.inject(ReactionsService).changes$.subscribe(changes);
    view.click('like');
    const first = http.expectOne({ method: 'PUT', url });
    const duplicate = mount();
    expectState(duplicate, summary('like', 11, 5));
    duplicate.click('dislike');
    view.click('like');
    duplicate.click('like');
    duplicate.click('dislike');
    vi.advanceTimersByTime(100);
    http.expectNone(url);
    expectState(view, summary('dislike', 10, 6));
    expectState(other, summary());

    first.flush(summary('like', 20, 5));
    const last = http.expectOne({ method: 'PUT', url });
    expect(last.request.body).toEqual({ value: 'dislike' });
    expectState(view, summary('dislike', 19, 6));
    expectState(duplicate, summary('dislike', 19, 6));
    const final = summary('dislike', 21, 8);
    last.flush(final);
    expectState(view, final);
    expectState(duplicate, final);
    expect(changes.mock.calls.filter(([change]) => change.kind === 'saved')).toHaveLength(1);
    expect(changes).toHaveBeenLastCalledWith(
      expect.objectContaining({ kind: 'saved', summary: final }),
    );
    subscription.unsubscribe();
  });

  it('coalesces the queue when the first response already matches the desired value', () => {
    const view = mount();
    const http = TestBed.inject(HttpTestingController);
    view.click('like');
    const first = http.expectOne({ method: 'PUT', url });
    view.click('dislike');
    view.click('like');
    first.flush(summary('like', 12, 6));
    http.expectNone(url);
    expectState(view, summary('like', 12, 6));
  });

  it('rolls back a failed queued change to the last server response', () => {
    const view = mount();
    const http = TestBed.inject(HttpTestingController);
    view.click('like');
    const first = http.expectOne({ method: 'PUT', url });
    view.click('like');
    first.flush(summary('like', 17, 5));
    expectState(view, summary(null, 16, 5));
    http.expectOne({ method: 'DELETE', url }).error(new ProgressEvent('error'));
    expectState(view, summary('like', 17, 5));
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('Volvé a intentarlo');
  });

  it('does not send a queued change after the account changes', () => {
    const view = mount();
    const http = TestBed.inject(HttpTestingController);
    view.click('like');
    const first = http.expectOne({ method: 'PUT', url });
    view.click('dislike');
    const user = createSession().user;
    auth.currentUser.set({ ...user, id: user.id + 1 });
    first.flush(summary('like', 17, 5));
    http.expectNone(url);
    expectState(view, summary());
  });

  it('shows full accessible counts and explains disabled buttons on own content', () => {
    const view = mount(summary(null, 1200, 3), true);
    const like = view.button('like');
    expect(like.disabled).toBe(true);
    expect(view.button('dislike').disabled).toBe(true);
    expect(like.textContent?.replace(/\s/g, ' ')).toContain('1,2 mil');
    expect(like.title).toBe('Me gusta (1200)');
    const describedBy = view.root.querySelector('[role="group"]')!.getAttribute('aria-describedby');
    expect(view.root.querySelector(`#${describedBy}`)?.textContent).toContain(
      'No podés reaccionar a tu propio contenido',
    );
    view.click('dislike');
    TestBed.inject(HttpTestingController).expectNone(url);
  });

  it('rolls back and disables both buttons after the API rejects an own reaction', () => {
    const view = mount();
    view.click('like');
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush(
        { extensions: { code: 'cannot_react_to_own_content' } },
        { status: 409, statusText: 'Conflict' },
      );
    expectState(view, summary());
    expect(view.button('like').disabled && view.button('dislike').disabled).toBe(true);
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('tu propio contenido');
  });

  it('rolls back a 404 and tells the parent which content to remove', () => {
    const view = mount();
    const unavailable = vi.fn();
    view.fixture.componentInstance.unavailable.subscribe(unavailable);
    view.click('like');
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush({ code: 'entry_not_found' }, { status: 404, statusText: 'Not Found' });
    expectState(view, summary());
    expect(unavailable).toHaveBeenCalledExactlyOnceWith(entry);
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain(
      'ya no está disponible',
    );
  });

  it('rolls back a 429 and honors Retry-After without retrying the click', () => {
    vi.useFakeTimers();
    const view = mount();
    const http = TestBed.inject(HttpTestingController);
    view.click('like');
    http
      .expectOne(url)
      .flush(
        { code: 'rate_limited' },
        {
          status: 429,
          statusText: 'Too Many Requests',
          headers: new HttpHeaders({ 'Retry-After': '2' }),
        },
      );
    expectState(view, summary());
    expect(view.button('like').disabled).toBe(true);
    expect(view.root.querySelector('[role="alert"]')?.textContent).toContain('2 segundos');
    vi.advanceTimersByTime(2000);
    view.fixture.detectChanges();
    expect(view.button('like').disabled).toBe(false);
    http.expectNone(url);
  });

  it('uses a fresh snapshot when the parent reloads the entry', () => {
    const view = mount();
    view.click('like');
    TestBed.inject(HttpTestingController)
      .expectOne(url)
      .flush(summary('like', 12, 5));
    expectState(view, summary('like', 12, 5));
    view.fixture.componentRef.setInput('summary', summary('like', 15, 9));
    expectState(view, summary('like', 15, 9));
  });
});
