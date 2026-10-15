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

describe('Layout logout action', () => {
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
    return fixture.nativeElement.querySelector('header button') as HTMLButtonElement;
  }

  it('hides the logout button without an active session', () => {
    // Arrange / Act
    fixture.detectChanges();

    // Assert
    expect(fixture.nativeElement.querySelector('header button')).toBeNull();
    expect(auth.logout).not.toHaveBeenCalled();
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
    expect(fixture.nativeElement.querySelector('header button')).toBeNull();
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
