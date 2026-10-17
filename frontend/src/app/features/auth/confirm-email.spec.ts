import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { createAuthMock, createSession, createTokenStorageMock } from './auth-test-helpers';
import { ConfirmEmailComponent } from './confirm-email';
import type { AuthMessage } from './models/password-recovery';
import { AuthService } from './services/auth';
import { TokenStorageService } from './services/token-storage';
import { routes } from '../../app.routes';

describe('ConfirmEmailComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailComponent>;
  let auth: ReturnType<typeof createAuthMock>;
  let storage: ReturnType<typeof createTokenStorageMock>;
  let response: Subject<AuthMessage>;
  let navigate: ReturnType<typeof vi.spyOn>;
  let route: { snapshot: { queryParamMap: ReturnType<typeof convertToParamMap> } };

  beforeEach(() => {
    localStorage.clear();
    auth = createAuthMock();
    storage = createTokenStorageMock();
    response = new Subject<AuthMessage>();
    auth.confirmEmail.mockReturnValue(response);
  });
  afterEach(() => {
    fixture?.destroy();
    response.complete();
    localStorage.clear();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });
  function render(
    params: Record<string, string | undefined> = {
      email: 'ana@example.com',
      token: 'url-safe-token',
    },
  ) {
    route = { snapshot: { queryParamMap: convertToParamMap(params) } };
    TestBed.configureTestingModule({
      imports: [ConfirmEmailComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: TokenStorageService, useValue: storage },
        { provide: ActivatedRoute, useValue: route },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(ConfirmEmailComponent);
    fixture.detectChanges();
  }

  it.each([
    {},
    { email: 'ana@example.com' },
    { token: 'token' },
    { email: ' ', token: 'token' },
    { email: 'ana@example.com', token: ' ' },
  ])('ConfirmEmail_WithMissingParameters_ShowsInvalidLinkAndResendWithoutRequest: %j', (params) => {
    // Arrange / Act
    render(params);
    // Assert
    expect(auth.confirmEmail).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'inválido o venció',
    );
    expect(fixture.nativeElement.querySelector('app-resend-confirmation input')).not.toBeNull();
  });

  it('ConfirmEmail_OnLoad_RequestsOnceAndReplacesHistoryWithoutQueryParams', () => {
    // Arrange / Act
    render();
    fixture.detectChanges();
    fixture.detectChanges();
    // Assert
    expect(auth.confirmEmail).toHaveBeenCalledExactlyOnceWith('ana@example.com', 'url-safe-token');
    expect(navigate).toHaveBeenCalledExactlyOnceWith([], {
      relativeTo: route,
      queryParams: {},
      replaceUrl: true,
    });
    expect(fixture.nativeElement.querySelector('[role="status"]').getAttribute('aria-busy')).toBe(
      'true',
    );
    expect(fixture.nativeElement.textContent).toContain('Estamos verificando');
  });

  it('ConfirmEmail_WithSuccess_ShowsLoginActionAndPreservesActiveSession', () => {
    // Arrange
    auth.isAuthenticated.set(true);
    auth.currentUser.set(createSession().user);
    render();
    // Act
    response.next({ message: 'confirmed' });
    response.complete();
    fixture.detectChanges();
    // Assert
    expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Email confirmado');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'Ya podés iniciar sesión',
    );
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe('/auth/login');
    expect(auth.clearSession).not.toHaveBeenCalled();
    expect(auth.restoreSession).not.toHaveBeenCalled();
    expect(auth.login).not.toHaveBeenCalled();
    expect(storage.save).not.toHaveBeenCalled();
    expect(storage.clear).not.toHaveBeenCalled();
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('ConfirmEmail_WithBadRequest_PrefillsResendEmailAndSendsNeutralRequest', () => {
    // Arrange
    render();
    auth.resendConfirmation.mockReturnValue(of({ message: 'private' }));
    // Act
    response.error(new HttpErrorResponse({ status: 400 }));
    fixture.detectChanges();
    // Assert
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'inválido o venció',
    );
    expect((fixture.nativeElement.querySelector('input') as HTMLInputElement).value).toBe(
      'ana@example.com',
    );
    (fixture.nativeElement.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(auth.resendConfirmation).toHaveBeenCalledExactlyOnceWith('ana@example.com');
    expect(fixture.nativeElement.querySelector('[role="status"]').textContent).toContain(
      'Si el email corresponde',
    );
    expect(fixture.nativeElement.textContent).not.toContain('private');
  });

  it.each([0, 500, 503])(
    'ConfirmEmail_WithTransientError_%s_AllowsOneExplicitRetryWithOriginalToken',
    (status) => {
      // Arrange
      render();
      // Act
      response.error(new HttpErrorResponse({ status }));
      fixture.detectChanges();
      // Assert
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'No pudimos confirmar tu email',
      );
      expect(fixture.nativeElement.querySelector('form')).toBeNull();
      expect(auth.confirmEmail).toHaveBeenCalledTimes(1);
      const retry = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
      expect(retry.textContent).toContain('Reintentar');
      response = new Subject<AuthMessage>();
      auth.confirmEmail.mockReturnValue(response);
      retry.click();
      fixture.detectChanges();
      expect(auth.confirmEmail).toHaveBeenCalledTimes(2);
      expect(auth.confirmEmail).toHaveBeenLastCalledWith('ana@example.com', 'url-safe-token');
      expect(fixture.nativeElement.querySelector('button')).toBeNull();
      response.next({ message: 'confirmed' });
      response.complete();
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('h1').textContent).toContain('Email confirmado');
    },
  );

  it('ConfirmationRoute_IsPublicAndLoadsConfirmationComponent', async () => {
    // Arrange
    const route = routes.find((entry) => entry.path === 'confirm-email');
    // Act
    const component = await (route!.loadComponent as () => Promise<unknown>)();
    // Assert
    expect(route?.canActivate).toBeUndefined();
    expect(component).toBe(ConfirmEmailComponent);
  });
});
