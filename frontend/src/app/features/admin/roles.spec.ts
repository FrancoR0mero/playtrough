import { OverlayContainer } from '@angular/cdk/overlay';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { AppRole } from '../../core/authorization/roles';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { AdminUser } from './models/admin-users';
import { AdminRolesComponent } from './roles';

const USERS: AdminUser[] = [
  { id: 1, username: 'ana_admin', email: 'ana@example.com', role: 'Admin', emailConfirmed: true },
  { id: 2, username: 'bruno', email: 'bruno@example.com', role: 'User', emailConfirmed: true },
  {
    id: 3,
    username: 'carla',
    email: 'carla@example.com',
    role: 'Moderator',
    emailConfirmed: false,
  },
];

describe('Admin roles screen', () => {
  let fixture: ComponentFixture<AdminRolesComponent>;
  let httpMock: HttpTestingController;
  let storage: TokenStorageService;
  const usersUrl = `${environment.apiUrl}/admin/users`;
  const changeUrl = `${usersUrl}/2/role`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AdminRolesComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.clear();
    storage.save({ ...createSession(), user: { ...createSession().user, role: 'Admin' } });
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AdminRolesComponent);
    fixture.detectChanges();
  });

  afterEach(() => {
    try {
      httpMock.verify();
    } finally {
      storage.clear();
      TestBed.resetTestingModule();
      vi.restoreAllMocks();
      vi.useRealTimers();
    }
  });

  function listRequest() {
    return httpMock.expectOne((req) => req.method === 'GET' && req.url === usersUrl);
  }

  function load(items = USERS, total = items.length, page = 1): void {
    listRequest().flush({ items, total, page, pageSize: 20 });
    fixture.detectChanges();
  }

  function roleSelect(id = 2): HTMLSelectElement {
    return fixture.nativeElement.querySelector(`#user-role-${id}`);
  }

  function screenButton(text: string): HTMLButtonElement {
    return Array.from(
      fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
    ).find((button) => button.textContent?.includes(text))!;
  }

  function choose(role: AppRole = 'Moderator', id = 2): void {
    const select = roleSelect(id);
    select.focus();
    select.value = role;
    select.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();
  }

  async function confirm(): Promise<void> {
    (document.querySelector('[data-action="confirm"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('renders loading, account data, role badges and confirmation state', () => {
    expect(fixture.nativeElement.textContent).toContain('Cargando usuarios');
    load();
    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(3);
    expect(rows[0].textContent).toContain('ana_admin');
    expect(rows[1].textContent).toContain('bruno@example.com');
    expect(rows[1].textContent).toContain('Email confirmado');
    expect(rows[2].textContent).toContain('Email sin confirmar');
    expect(roleSelect().value).toBe('User');
    expect(Array.from(roleSelect().options).map((option) => option.value)).toEqual([
      'User',
      'Moderator',
      'Admin',
    ]);
    expect(fixture.nativeElement.textContent).not.toContain('Cargando usuarios');
  });

  it('disables the current account and associates an accessible explanation', () => {
    load();
    const own = roleSelect(1);
    expect(own.disabled).toBe(true);
    const description = fixture.nativeElement.querySelector(
      `#${own.getAttribute('aria-describedby')}`,
    );
    expect(description.textContent).toContain('No podés cambiar tu propio rol');
    choose('User', 1);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    httpMock.expectNone((req) => req.method === 'PUT');
  });

  it('debounces search and sends the partial query', () => {
    vi.useFakeTimers();
    load();
    const input = fixture.nativeElement.querySelector('#user-search') as HTMLInputElement;
    input.value = 'bru';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(200);
    httpMock.expectNone((req) => req.method === 'GET');
    input.value = 'bruno';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(299);
    httpMock.expectNone((req) => req.method === 'GET');
    vi.advanceTimersByTime(1);
    const request = listRequest();
    expect(request.request.params.get('search')).toBe('bruno');
    expect(request.request.params.get('page')).toBe('1');
    request.flush({ items: [USERS[1]], total: 1, page: 1, pageSize: 20 });
  });

  it('paginates and resets to page one after changing the role filter', () => {
    load(USERS, 41);
    expect(screenButton('Anterior').disabled).toBe(true);
    screenButton('Siguiente').click();
    const request = listRequest();
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: USERS, total: 41, page: 2, pageSize: 20 });
    fixture.detectChanges();
    const filter = fixture.nativeElement.querySelector('#role-filter') as HTMLSelectElement;
    filter.value = 'Admin';
    filter.dispatchEvent(new Event('change'));
    const filtered = listRequest();
    expect(filtered.request.params.get('role')).toBe('Admin');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush({ items: [USERS[0]], total: 1, page: 1, pageSize: 20 });
    fixture.detectChanges();
    expect(screenButton('Siguiente').disabled).toBe(true);
  });

  it('cancels an obsolete filter request so its results cannot overwrite the latest query', () => {
    load();
    const filter = fixture.nativeElement.querySelector('#role-filter') as HTMLSelectElement;
    filter.value = 'User';
    filter.dispatchEvent(new Event('change'));
    const obsolete = listRequest();
    filter.value = 'Moderator';
    filter.dispatchEvent(new Event('change'));
    expect(obsolete.cancelled).toBe(true);
    load([USERS[2]]);
    expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(fixture.nativeElement.querySelector('tbody')?.textContent).toContain('carla');
  });

  it('shows an empty state', () => {
    load([]);
    expect(fixture.nativeElement.textContent).toContain('No encontramos usuarios');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it.each([403, 500])('offers retry when the list fails with %s', (status) => {
    listRequest().flush(null, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      status === 403 ? 'No tenés permisos para esta acción' : 'No pudimos cargar los usuarios',
    );
    screenButton('Reintentar carga').click();
    load();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('does not open a dialog or call the API when selecting the current role', () => {
    load();
    choose('User');
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    httpMock.expectNone((req) => req.method === 'PUT');
  });

  it('cancels without sending a role change and restores the selector', async () => {
    load();
    choose();
    expect(document.querySelector('[role="dialog"]')?.textContent).toContain('bruno');
    expect(roleSelect().value).toBe('User');
    (document.querySelector('[data-action="cancel"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(roleSelect().value).toBe('User');
    httpMock.expectNone((req) => req.method === 'PUT');
  });

  it('confirms a change, disables the row while saving and uses the returned user', async () => {
    load();
    choose();
    await confirm();
    const request = httpMock.expectOne(changeUrl);
    expect(request.request.body).toEqual({ role: 'Moderator' });
    expect(roleSelect().disabled).toBe(true);
    expect(
      fixture.nativeElement.querySelector('[data-user-id="2"]').getAttribute('aria-busy'),
    ).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Guardando cambio');
    request.flush({
      ...USERS[1],
      username: 'bruno_updated',
      role: 'Moderator',
      emailConfirmed: false,
    });
    fixture.detectChanges();
    expect(roleSelect().disabled).toBe(false);
    expect(roleSelect().value).toBe('Moderator');
    expect(fixture.nativeElement.querySelector('[data-user-id="2"]')?.textContent).toContain(
      'bruno_updated',
    );
    expect(fixture.nativeElement.querySelector('[data-user-id="2"]')?.textContent).toContain(
      'Email sin confirmar',
    );
    expect(fixture.nativeElement.querySelector('[role="status"]')?.textContent).toContain(
      'se actualizó a Moderator',
    );
  });

  it.each([
    [400, 'cannot_change_own_role', 'No podés cambiar tu propio rol'],
    [400, 'last_admin', 'No se puede quitar el rol al último administrador'],
    [400, 'invalid_role', 'El rol seleccionado no es válido'],
    [400, 'unexpected_code', 'No pudimos cambiar el rol'],
    [403, '', 'No tenés permisos para esta acción'],
    [404, 'user_not_found', 'El usuario ya no existe'],
  ])('shows error %s/%s and reloads the real list', async (status, code, message) => {
    load();
    choose();
    await confirm();
    httpMock.expectOne(changeUrl).flush({ code }, { status: Number(status), statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(message);
    load(status === 404 ? [USERS[0], USERS[2]] : USERS);
    if (status === 404) expect(roleSelect()).toBeNull();
    else expect(roleSelect().value).toBe('User');
  });

  it('also accepts an error code nested under extensions', async () => {
    load();
    choose();
    await confirm();
    httpMock
      .expectOne(changeUrl)
      .flush({ extensions: { code: 'last_admin' } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'último administrador',
    );
    load();
  });

  it.each([0, 500])('shows a generic retryable error for status %s', async (status) => {
    load();
    choose();
    await confirm();
    const request = httpMock.expectOne(changeUrl);
    if (status === 0) request.error(new ProgressEvent('error'));
    else request.flush(null, { status, statusText: 'Error' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'No pudimos cambiar el rol',
    );
    expect(roleSelect().disabled).toBe(false);
    screenButton('Reintentar cambio').click();
    fixture.detectChanges();
    await confirm();
    httpMock.expectOne(changeUrl).flush({ ...USERS[1], role: 'Moderator' });
    fixture.detectChanges();
    expect(roleSelect().value).toBe('Moderator');
  });

  it('leaves 401 handling to the interceptor', async () => {
    load();
    choose();
    await confirm();
    httpMock.expectOne(changeUrl).flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    httpMock.expectNone((req) => req.method === 'GET');
  });

  it('reloads after a success that removes the user from the active role filter', async () => {
    load();
    const filter = fixture.nativeElement.querySelector('#role-filter') as HTMLSelectElement;
    filter.value = 'User';
    filter.dispatchEvent(new Event('change'));
    load([USERS[1]]);
    choose();
    await confirm();
    httpMock.expectOne(changeUrl).flush({ ...USERS[1], role: 'Moderator' });
    load([]);
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it('corrects an empty last page after a user disappears', () => {
    load(USERS, 21);
    screenButton('Siguiente').click();
    listRequest().flush({ items: [], total: 20, page: 2, pageSize: 20 });
    const corrected = listRequest();
    expect(corrected.request.params.get('page')).toBe('1');
    corrected.flush({ items: USERS, total: 20, page: 1, pageSize: 20 });
  });

  it('traps modal focus, closes with Escape and restores the selector focus', async () => {
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(100);
    vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(100);
    load();
    choose();
    await fixture.whenStable();
    const dialog = document.querySelector('[role="dialog"]') as HTMLElement;
    const cancel = dialog.querySelector('[data-action="cancel"]') as HTMLButtonElement;
    const confirmButton = dialog.querySelector('[data-action="confirm"]') as HTMLButtonElement;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.getAttribute('aria-labelledby')).toBe('role-change-title');
    expect(dialog.getAttribute('aria-describedby')).toBe('role-change-description');
    await vi.waitFor(() => expect(document.activeElement).toBe(cancel));
    const anchors = TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('.cdk-focus-trap-anchor');
    expect(anchors).toHaveLength(2);
    anchors[0].focus();
    expect(document.activeElement).toBe(confirmButton);
    anchors[1].focus();
    expect(document.activeElement).toBe(cancel);
    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, bubbles: true }),
    );
    await fixture.whenStable();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(document.activeElement).toBe(roleSelect());
    expect(roleSelect().value).toBe('User');
    httpMock.expectNone((req) => req.method === 'PUT');
  });
});
