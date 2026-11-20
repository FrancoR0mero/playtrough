import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NavigationEnd, provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { filter, firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { routes } from '../../app.routes';
import { authGuard } from '../../core/guards/auth-guard';
import { GroupCreateComponent } from './group-create';
import { GroupDetailComponent } from './group-detail';
import { GroupsComponent } from './groups';

const url = `${environment.apiUrl}/groups`;
const group = {
  id: 42,
  name: 'Club Retro',
  description: 'Juegos clásicos',
  topic: 'Retro',
  privacy: 'private',
};
const ownerMember = {
  userId: 1,
  username: 'propietaria',
  avatarUrl: null,
  joinedAt: '2026-11-19T12:00:00Z',
  isOwner: true,
};
const review = {
  id: 15,
  gameId: 9,
  gameName: 'Portal',
  username: 'lectora',
  rating: 4.5,
  text: 'Me gustó el diseño de los niveles.',
  createdAt: '2026-11-19T12:00:00Z',
  editedAt: null,
};

describe('Groups', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'groups', component: GroupsComponent },
          { path: 'groups/new', component: GroupCreateComponent },
          { path: 'groups/:id', component: GroupDetailComponent },
        ]),
      ],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('protects group routes, filters discovery and appends results using the same filters', async () => {
    for (const path of ['groups', 'groups/new', 'groups/:id']) {
      expect(routes.find((route) => route.path === path)?.canActivate).toEqual([authGuard]);
    }
    const harness = await RouterTestingHarness.create('/groups');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/topics').flush(['Retro']);
    http.expectOne(url).flush({ items: [], nextCursor: null });
    harness.detectChanges();
    expect(root.textContent).toContain('No encontramos grupos');

    changeField(root, 'q', ' club ', 'input');
    changeField(root, 'topic', 'Retro', 'change');
    submit(root);
    http.expectOne(url + '?q=club&topic=Retro').flush({ items: [group], nextCursor: 42 });
    harness.detectChanges();
    expect(root.querySelector('a[href="/groups/42"]')?.textContent).toContain('Privado');
    const more = Array.from(root.querySelectorAll('button')).find(
      (button) => button.textContent?.trim() === 'Cargar más',
    )!;
    more.click();
    http.expectOne(url + '?q=club&topic=Retro&cursor=42').flush({
      items: [{ ...group, id: 41, name: 'Otro club' }],
      nextCursor: null,
    });
    harness.detectChanges();
    expect(
      Array.from(root.querySelectorAll('li a')).map((link) => link.getAttribute('href')),
    ).toEqual(['/groups/42', '/groups/41']);
  });

  it('validates a group, sends the normalized draft and opens the created group', async () => {
    const harness = await RouterTestingHarness.create('/groups/new');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/topics').flush(['Retro']);
    harness.detectChanges();
    submit(root);
    harness.detectChanges();
    expect(root.textContent).toContain('Ingresá un nombre');
    http.expectNone(url);

    changeField(root, 'name', ' Club Retro ', 'input');
    changeField(root, 'description', ' Juegos clásicos ', 'input');
    changeField(root, 'topic', 'Retro', 'change');
    changeField(root, 'privacy', 'private', 'change');
    submit(root);
    const request = http.expectOne({ method: 'POST', url });
    expect(request.request.body).toEqual({
      name: 'Club Retro',
      description: 'Juegos clásicos',
      topic: 'Retro',
      privacy: 'private',
    });
    const finished = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    request.flush(group, { status: 201, statusText: 'Created' });
    await finished;
    http.expectOne(url + '/42').flush({ ...group, membership: 'owner' });
    harness.detectChanges();
    http.expectOne(url + '/42/members').flush({ items: [ownerMember], nextCursor: null });
    http.expectOne(url + '/42/join-requests').flush({ items: [], nextCursor: null });
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Club Retro');
  });

  it('lets a private visitor request and cancel access while keeping the member list hidden', async () => {
    const harness = await RouterTestingHarness.create('/groups/42');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/42').flush({ ...group, membership: 'none' });
    harness.detectChanges();
    http.expectNone(url + '/42/members');
    button(root, 'Solicitar acceso').click();
    http.expectOne({ method: 'PUT', url: url + '/42/membership' }).flush({ status: 'pending' });
    harness.detectChanges();
    expect(root.textContent).toContain('Solicitud pendiente');
    http.expectNone(url + '/42/members');

    button(root, 'Cancelar solicitud').click();
    harness.detectChanges();
    confirmDialog().click();
    http.expectOne({ method: 'DELETE', url: url + '/42/membership' }).flush(null, {
      status: 204,
      statusText: 'No Content',
    });
    http.expectOne(url + '/42').flush({ ...group, membership: 'none' });
    harness.detectChanges();
    expect(button(root, 'Solicitar acceso')).toBeTruthy();
  });

  it('shows owner requests, approves after confirmation and refreshes the members', async () => {
    const harness = await RouterTestingHarness.create('/groups/42');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/42').flush({ ...group, membership: 'owner' });
    harness.detectChanges();
    http.expectOne(url + '/42/members').flush({ items: [ownerMember], nextCursor: null });
    http.expectOne(url + '/42/join-requests').flush({
      items: [
        {
          id: 3,
          userId: 2,
          username: 'solicitante',
          avatarUrl: null,
          createdAt: ownerMember.joinedAt,
        },
      ],
      nextCursor: null,
    });
    harness.detectChanges();
    button(root, 'Aprobar').click();
    harness.detectChanges();
    const decisionUrl = url + '/42/join-requests/3/decision';
    http.expectNone(decisionUrl);
    confirmDialog().click();
    const decision = http.expectOne({ method: 'POST', url: decisionUrl });
    expect(decision.request.body).toEqual({ decision: 'approve' });
    decision.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(url + '/42/members').flush({
      items: [ownerMember, { ...ownerMember, userId: 2, username: 'solicitante', isOwner: false }],
      nextCursor: null,
    });
    http.expectOne(url + '/42/join-requests').flush({ items: [], nextCursor: null });
    harness.detectChanges();
    expect(root.querySelector('[aria-label="Expulsar a solicitante"]')).not.toBeNull();
    expect(root.textContent).toContain('No hay solicitudes pendientes');
  });

  it('finishes leaving when a member list request loses access during the operation', async () => {
    const harness = await RouterTestingHarness.create('/groups/42');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/42').flush({ ...group, membership: 'member' });
    harness.detectChanges();
    const members = http.expectOne(url + '/42/members');
    button(root, 'Salir del grupo').click();
    harness.detectChanges();
    confirmDialog().click();

    const leave = http.expectOne({ method: 'DELETE', url: url + '/42/membership' });
    members.flush(null, { status: 404, statusText: 'Not Found' });
    http.expectNone(url + '/42');
    leave.flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne(url + '/42').flush({ ...group, membership: 'none' });
    harness.detectChanges();
    expect(button(root, 'Solicitar acceso').disabled).toBe(false);
    expect(root.querySelector('app-group-people')).toBeNull();
  });

  it('opens the reviews tab for a member and pages the original reviews', async () => {
    const harness = await RouterTestingHarness.create('/groups/42');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/42').flush({ ...group, membership: 'member' });
    harness.detectChanges();
    http.expectOne(url + '/42/members').flush({ items: [ownerMember], nextCursor: null });
    harness.detectChanges();
    http.expectNone(url + '/42/reviews');
    const tab = root.querySelector<HTMLAnchorElement>('a[href="/groups/42?tab=reviews"]')!;
    const finished = firstValueFrom(
      TestBed.inject(Router).events.pipe(filter((event) => event instanceof NavigationEnd)),
    );
    tab.click();
    await finished;
    harness.detectChanges();
    expect(tab.getAttribute('aria-current')).toBe('page');
    http.expectOne(url + '/42/reviews').flush({ items: [review], nextCursor: 15 });
    harness.detectChanges();
    expect(root.querySelector('a[href="/games/local/9/reviews"]')?.textContent).toContain('Portal');
    expect(root.querySelector('#group-review-15')?.textContent).toContain(review.text);

    button(root, 'Cargar más reseñas').click();
    http.expectOne(url + '/42/reviews?cursor=15').flush({
      items: [{ ...review, id: 12, username: 'otro', text: null }],
      nextCursor: null,
    });
    harness.detectChanges();
    expect(root.querySelectorAll('app-review-content')).toHaveLength(2);
  });

  it('shows an empty page and hides the reviews tab if membership is lost', async () => {
    const harness = await RouterTestingHarness.create('/groups/42?tab=reviews');
    const http = TestBed.inject(HttpTestingController);
    const root = harness.routeNativeElement!;
    http.expectOne(url + '/42').flush({ ...group, membership: 'member' });
    harness.detectChanges();
    http.expectOne(url + '/42/reviews').flush(null, { status: 503, statusText: 'Unavailable' });
    harness.detectChanges();
    expect(root.textContent).toContain('No pudimos cargar las reseñas');
    button(root, 'Reintentar').click();
    http.expectOne(url + '/42/reviews').flush({ items: [], nextCursor: null });
    harness.detectChanges();
    expect(root.textContent).toContain('Todavía no hay reseñas');

    await harness.navigateByUrl('/groups/42');
    http.expectOne(url + '/42/members').flush({ items: [], nextCursor: null });
    await harness.navigateByUrl('/groups/42?tab=reviews');
    http.expectOne(url + '/42/reviews').flush(null, { status: 404, statusText: 'Not Found' });
    http.expectOne(url + '/42').flush({ ...group, membership: 'removed' });
    harness.detectChanges();
    http.expectNone(url + '/42/reviews');
    expect(root.querySelector('app-group-reviews')).toBeNull();
    expect(root.textContent).toContain('El propietario te retiró del grupo');
  });
});

function button(root: HTMLElement, title: string): HTMLButtonElement {
  return Array.from(root.querySelectorAll('button')).find(
    (item) => item.textContent?.trim() === title,
  )!;
}

function confirmDialog(): HTMLButtonElement {
  return document.querySelector<HTMLButtonElement>(
    'app-group-confirmation [data-action="confirm"]',
  )!;
}

function changeField(root: HTMLElement, name: string, value: string, event: string): void {
  const field = root.querySelector<HTMLInputElement>(`[formcontrolname="${name}"]`)!;
  field.value = value;
  field.dispatchEvent(new Event(event, { bubbles: true }));
}

function submit(root: HTMLElement): void {
  root
    .querySelector('form')!
    .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}
