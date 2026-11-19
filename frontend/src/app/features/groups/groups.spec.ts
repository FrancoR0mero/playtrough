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
    http.expectOne(url + '/42').flush(group);
    harness.detectChanges();
    expect(harness.routeNativeElement?.querySelector('h1')?.textContent).toContain('Club Retro');
  });
});

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
