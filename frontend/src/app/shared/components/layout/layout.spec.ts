import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import {
  createAuthMock,
  createSession,
  createTokenStorageMock,
} from '../../../features/auth/auth-test-helpers';
import { AuthService } from '../../../features/auth/services/auth';
import { TokenStorageService } from '../../../features/auth/services/token-storage';
import { LayoutComponent } from './layout';
import { NAVIGATION_ITEMS } from './layout-navigation';
import { RateLimitNoticeService } from '../../services/rate-limit-notice';

describe('Layout navigation and logout', () => {
  let fixture: ComponentFixture<LayoutComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let logoutResult: Subject<void>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    localStorage.removeItem('playtrough.session');
    auth = createAuthMock();
    logoutResult = new Subject<void>();
    auth.logout.mockReturnValue(logoutResult.asObservable());
    TestBed.configureTestingModule({
      imports: [LayoutComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: createTokenStorageMock() },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(LayoutComponent);
  });

  afterEach(() => {
    logoutResult.complete();
    fixture.destroy();
    localStorage.removeItem('playtrough.session');
    vi.restoreAllMocks();
  });

  function renderAuthenticated(): HTMLButtonElement {
    auth.isAuthenticated.set(true);
    auth.currentUser.set(createSession().user);
    fixture.detectChanges();
    return fixture.nativeElement.querySelector('button[data-action="logout"]') as HTMLButtonElement;
  }

  it('shows and dismisses the shared guard 429 alert without logout or navigation', () => {
    renderAuthenticated();
    TestBed.inject(RateLimitNoticeService).show(90);
    fixture.detectChanges();
    const alert = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(alert.textContent).toContain(
      'Demasiados intentos. Probá de nuevo en 1 minuto y 30 segundos',
    );
    (alert.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.logout).not.toHaveBeenCalled();
    expect(auth.clearSession).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('hides the logout button without an active session', () => {
    // Arrange / Act
    fixture.detectChanges();

    // Assert
    expect(fixture.nativeElement.querySelector('button[data-action="logout"]')).toBeNull();
    expect(auth.logout).not.toHaveBeenCalled();
  });

  it('provides mobile destinations and a disabled reviews placeholder only when signed in', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.mobile-game-navigation')).toBeNull();
    renderAuthenticated();
    const nav = fixture.nativeElement.querySelector('.mobile-game-navigation') as HTMLElement;
    expect(Array.from(nav.querySelectorAll('a')).map((link) => link.getAttribute('href'))).toEqual([
      '/games',
      '/library',
      '/profile',
    ]);
    const reviews = nav.querySelector('button')!;
    expect(reviews.disabled).toBe(true);
    expect(reviews.getAttribute('aria-label')).toBe('Reseñas, próximamente');
    expect(reviews.textContent).toContain('Próximamente');
    expect(
      fixture.nativeElement
        .querySelector('[aria-controls="primary-navigation"]')
        .classList.contains('hidden'),
    ).toBe(true);
  });

  it('shows login and registration links without a session', () => {
    fixture.detectChanges();
    const nav = fixture.nativeElement.querySelector('nav') as HTMLElement;
    expect(nav.querySelector('a[href="/auth/login"]')?.textContent).toContain('Iniciar sesión');
    expect(nav.querySelector('a[href="/auth/register"]')?.textContent).toContain('Registrarse');
    expect(nav.querySelector('a[href="/library"]')).toBeNull();
    expect(nav.querySelector('[aria-label="Rol de la cuenta"]')).toBeNull();
  });

  it.each(['User', 'Moderator', 'Admin'])(
    'shows account details and only enabled navigation for %s',
    (role) => {
      auth.isAuthenticated.set(true);
      auth.currentUser.set({ ...createSession().user, username: 'franco', role });
      fixture.detectChanges();
      const nav = fixture.nativeElement.querySelector('nav') as HTMLElement;
      expect(nav.textContent).toContain('franco');
      expect(nav.querySelector('[aria-label="Rol de la cuenta"]')?.textContent?.trim()).toBe(
        role === 'User' ? undefined : role,
      );
      expect(nav.querySelector('a[href="/library"]')?.textContent).toContain('Mi biblioteca');
      expect(nav.querySelector('button[data-action="logout"]')).not.toBeNull();
      expect(nav.querySelector('a[href="/auth/login"]')).toBeNull();
      expect(nav.querySelector('a[href="/auth/register"]')).toBeNull();
      expect(nav.querySelector('a[href="/admin/roles"]') !== null).toBe(role === 'Admin');
      expect(nav.querySelector('a[href="/moderation"]')).toBeNull();
    },
  );

  it('enables Roles for Admin and leaves Moderación disabled', () => {
    expect(NAVIGATION_ITEMS.find((item) => item.label === 'Roles')).toMatchObject({
      path: '/admin/roles',
      minRole: 'Admin',
      enabled: true,
    });
    expect(NAVIGATION_ITEMS.find((item) => item.label === 'Moderación')).toMatchObject({
      minRole: 'Moderator',
      enabled: false,
    });
  });

  it('updates account details and guest links as the session changes', () => {
    renderAuthenticated();
    auth.currentUser.set({ ...createSession().user, username: 'new_username', role: 'Admin' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav')?.textContent).toContain('new_username');
    expect(
      fixture.nativeElement.querySelector('[aria-label="Rol de la cuenta"]')?.textContent?.trim(),
    ).toBe('Admin');
    auth.isAuthenticated.set(false);
    auth.currentUser.set(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('nav a[href="/auth/login"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('button[data-action="logout"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Rol de la cuenta"]')).toBeNull();
  });

  it('provides a native keyboard-accessible disclosure button with matching ARIA state', () => {
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector(
      'button[aria-controls]',
    ) as HTMLButtonElement;
    const nav = fixture.nativeElement.querySelector('nav') as HTMLElement;
    expect(button.type).toBe('button');
    expect(button.tabIndex).toBe(0);
    expect(button.getAttribute('aria-controls')).toBe(nav.id);
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(button.textContent).toContain('Abrir menú');
    expect(nav.classList.contains('hidden')).toBe(true);
    expect(nav.classList.contains('md:flex')).toBe(true);
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.textContent).toContain('Cerrar menú');
    expect(nav.classList.contains('hidden')).toBe(false);
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(nav.classList.contains('hidden')).toBe(true);
  });

  it('closes the mobile navigation with Escape and returns focus to its toggle', () => {
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector(
      'button[aria-controls]',
    ) as HTMLButtonElement;
    const link = fixture.nativeElement.querySelector('nav a') as HTMLAnchorElement;
    button.click();
    fixture.detectChanges();
    link.focus();
    expect(document.activeElement).toBe(link);
    link.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(document.activeElement).toBe(button);
  });

  it('closes the mobile navigation after choosing a link', () => {
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector(
      'button[aria-controls]',
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('nav a') as HTMLAnchorElement).click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-expanded')).toBe('false');
  });

  it('shows a native button with clear text only while authenticated', () => {
    // Arrange / Act
    const button = renderAuthenticated();

    // Assert
    expect(button.type).toBe('button');
    expect(button.textContent?.trim()).toBe('Cerrar sesión');
    expect(button.disabled).toBe(false);
    expect(button.tabIndex).toBe(0);
    auth.isAuthenticated.set(false);
    auth.currentUser.set(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button[data-action="logout"]')).toBeNull();
  });

  it('calls logout once, disables the button while pending and redirects to a fresh login', async () => {
    // Arrange
    const button = renderAuthenticated();

    // Act
    button.click();
    fixture.detectChanges();
    button.click();
    button.dispatchEvent(new MouseEvent('click', { bubbles: true }));

    // Assert pending state and duplicate protection.
    expect(auth.logout).toHaveBeenCalledExactlyOnceWith();
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.textContent).toContain('Cerrando sesión');
    expect(navigate).not.toHaveBeenCalled();

    // Complete the asynchronous logout; AuthService owns local cleanup and error suppression.
    logoutResult.next();
    logoutResult.complete();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(navigate).toHaveBeenCalledExactlyOnceWith('/auth/login', { replaceUrl: true });
    expect(button.disabled).toBe(false);
  });

  it('redirects when logout completes without emitting a value', async () => {
    // Arrange
    const button = renderAuthenticated();
    button.click();

    // Act
    logoutResult.complete();
    await fixture.whenStable();

    // Assert
    expect(navigate).toHaveBeenCalledExactlyOnceWith('/auth/login', { replaceUrl: true });
  });
});
