import { Dialog } from '@angular/cdk/dialog';
import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, Router, provideRouter } from '@angular/router';
import { filter, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { TimelineEntryCardComponent } from './timeline-entry-card';
import { timelineEntry } from './timeline-test-helpers';

@Component({ template: '<h1>Perfil</h1>' })
class TestProfileComponent {}

const url = `${environment.apiUrl}/timeline/entries/42/likes`;
const liker = (username: string) => ({
  user: { username, avatarUrl: null },
  createdAt: '2026-03-12T12:00:00Z',
});

describe('Entry likes dialog', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([{ path: 'profile/:username', component: TestProfileComponent }]),
      ],
    });
  });
  afterEach(() => {
    TestBed.inject(Dialog).closeAll();
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  function setup(likeCount: number, own = false) {
    const fixture = TestBed.createComponent(TimelineEntryCardComponent);
    const root = fixture.nativeElement as HTMLElement;
    fixture.componentRef.setInput('entry', timelineEntry({ id: 42, likeCount, dislikeCount: 3 }));
    fixture.componentRef.setInput('perspective', own ? 'self' : 'other');
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    const dialog = () => overlay.querySelector<HTMLElement>('[role="dialog"]')!;
    const trigger = () => root.querySelector<HTMLButtonElement>('[data-view-likes]')!;
    const button = (text: string) =>
      Array.from(dialog().querySelectorAll('button')).find(
        (element) => element.textContent?.trim() === text,
      )!;
    const open = () => {
      trigger().focus();
      trigger().click();
      TestBed.tick();
    };
    const page = (users: string[], cursor?: string, nextCursor: string | null = null) => {
      http
        .expectOne({
          method: 'GET',
          url: `${url}?${cursor ? 'cursor=' + cursor + '&' : ''}limit=20`,
        })
        .flush({ items: users.map(liker), nextCursor, hasMore: !!nextCursor });
      TestBed.tick();
    };
    return { fixture, root, http, overlay, dialog, trigger, button, open, page };
  }

  it('shows the separate button only when there are likes, and dislike never opens a dialog', () => {
    const f = setup(0);
    expect(f.trigger()).toBeNull();
    const dislike = f.root.querySelector<HTMLButtonElement>('[data-reaction="dislike"]')!;
    expect(dislike.getAttribute('aria-haspopup')).toBeNull();
    dislike.click();
    f.http
      .expectOne({ method: 'PUT', url: url.replace('/likes', '/reaction') })
      .flush({ myReaction: 'dislike', likeCount: 0, dislikeCount: 4 });
    TestBed.tick();
    expect(f.dialog()).toBeNull();
    expect(f.trigger()).toBeNull();

    f.root.querySelector<HTMLButtonElement>('[data-reaction="like"]')!.click();
    TestBed.tick();
    expect(f.trigger().disabled).toBe(true);
    f.http
      .expectOne({ method: 'PUT', url: url.replace('/likes', '/reaction') })
      .flush({ myReaction: 'like', likeCount: 1, dislikeCount: 3 });
    TestBed.tick();
    expect(f.trigger().disabled).toBe(false);
    expect(f.trigger().getAttribute('aria-label')).toBe('Ver quiénes dieron Me gusta (1)');
    f.http.expectNone((request) => request.url === url);
  });

  it('opens for own entries and paginates once per click without duplicate users', () => {
    const f = setup(3, true);
    expect(f.root.querySelector<HTMLButtonElement>('[data-reaction="like"]')!.disabled).toBe(true);
    f.open();
    f.trigger().click();
    expect(TestBed.inject(Dialog).openDialogs).toHaveLength(1);
    expect(f.dialog().textContent).toContain('Cargando Me gusta');
    expect(f.dialog().textContent).toContain('Solo se muestran los me gusta');
    f.page(['ana', 'bea'], undefined, 'v1.more');
    const more = f.button('Cargar más');
    more.click();
    more.click();
    f.page(['bea', 'claudio'], 'v1.more');
    expect(
      Array.from(f.dialog().querySelectorAll('li a')).map((link) => link.getAttribute('href')),
    ).toEqual(['/profile/ana', '/profile/bea', '/profile/claudio']);
    expect(f.dialog().querySelectorAll('app-user-avatar')).toHaveLength(3);
    expect(f.button('Cargar más')).toBeUndefined();
  });

  it('shows the empty state even if the card displays a positive count', () => {
    const f = setup(2);
    f.open();
    f.page([]);
    expect(f.dialog().textContent).toContain('No hay Me gusta visibles');
  });

  it('retries a failed page after Retry-After, and clears stale users if the entry becomes unavailable', () => {
    vi.useFakeTimers();
    const f = setup(3);
    f.open();
    f.page(['ana'], undefined, 'v1.more');
    f.button('Cargar más').click();
    const next = `${url}?cursor=v1.more&limit=20`;
    f.http
      .expectOne(next)
      .flush({}, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '2' } });
    TestBed.tick();
    expect(f.dialog().querySelector('[role="alert"]')?.textContent).toContain('2 segundos');
    expect(f.button('Reintentar').disabled).toBe(true);
    vi.advanceTimersByTime(2000);
    TestBed.tick();
    f.button('Reintentar').click();
    f.http
      .expectOne(next)
      .flush({ code: 'entry_not_found' }, { status: 404, statusText: 'Not Found' });
    TestBed.tick();
    expect(f.dialog().querySelector('[role="alert"]')?.textContent).toContain(
      'Esta entrada ya no está disponible',
    );
    expect(f.dialog().querySelector('a')).toBeNull();
    expect(f.button('Reintentar')).toBeUndefined();
  });

  it('traps focus, closes with Escape or the backdrop, and restores focus', async () => {
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(100);
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(100);
    const f = setup(2);
    f.open();
    f.page(['ana']);
    expect(f.dialog().getAttribute('aria-modal')).toBe('true');
    expect(f.dialog().getAttribute('aria-labelledby')).toBe('entry-likes-title');
    const close = f.dialog().querySelector('[data-close-likes]');
    await vi.waitFor(() => expect(document.activeElement).toBe(close));
    const anchors = f.overlay.querySelectorAll<HTMLElement>('.cdk-focus-trap-anchor');
    expect(anchors).toHaveLength(2);
    anchors[0].focus();
    expect(document.activeElement).toBe(f.dialog().querySelector('a'));
    anchors[1].focus();
    expect(document.activeElement).toBe(close);
    f.dialog().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }),
    );
    await f.fixture.whenStable();
    expect(f.dialog()).toBeNull();
    expect(document.activeElement).toBe(f.trigger());

    f.open();
    const request = f.http.expectOne(`${url}?limit=20`);
    f.overlay.querySelector<HTMLElement>('.cdk-overlay-backdrop')!.click();
    await f.fixture.whenStable();
    expect(request.cancelled).toBe(true);
    expect(f.dialog()).toBeNull();
    expect(document.activeElement).toBe(f.trigger());
  });

  it('links to profiles and closes when navigating without taking focus back to the card', async () => {
    const f = setup(1);
    f.open();
    f.page(['ana']);
    const profile = f.dialog().querySelector('a')!;
    expect(profile.getAttribute('href')).toBe('/profile/ana');
    const focus = vi.spyOn(f.trigger(), 'focus');
    const router = TestBed.inject(Router);
    const navigated = firstValueFrom(
      router.events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    profile.click();
    await navigated;
    expect(router.url).toBe('/profile/ana');
    expect(f.dialog()).toBeNull();
    expect(focus).not.toHaveBeenCalled();
  });
});
