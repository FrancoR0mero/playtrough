import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { LoginResponse } from './models/login';
import { RegisterComponent } from './register';
import { TokenStorageService } from './services/token-storage';

describe('Authentication form', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let storage: TokenStorageService;
  const session = (): LoginResponse => ({
    accessToken: 'access',
    refreshToken: 'refresh',
    expiresAt: new Date(Date.now() + 900_000).toISOString(),
    user: { id: 1, username: 'ana', email: 'ana@example.com', role: 'User' },
  });

  async function render(mode = 'login', returnUrl: string | null = null): Promise<void> {
    const params = convertToParamMap(returnUrl ? { returnUrl } : {});
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            data: of({ mode }),
            queryParamMap: of(params),
            snapshot: { data: { mode }, queryParamMap: params },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(RegisterComponent);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function input(name: string, value: string): HTMLInputElement {
    const element = fixture.nativeElement.querySelector(
      `[formControlName="${name}"]`,
    ) as HTMLInputElement;
    element.value = value;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    fixture.detectChanges();
    return element;
  }
  function submit(): void {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    fixture.detectChanges();
  }
  beforeEach(() => {
    storage = new TokenStorageService();
    storage.clear();
  });
  afterEach(() => {
    http?.verify();
    fixture?.destroy();
    storage.clear();
    vi.restoreAllMocks();
  });

  it.each([null, '/library?tab=playing'])(
    'redirects a successful registration to login, preserving returnUrl %s',
    async (returnUrl) => {
      await render('register', returnUrl);
      input('email', 'ana@example.com');
      input('username', 'ana');
      input('password', 'x');
      expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
      input('password', 'Str0ngPass123');
      submit();
      http
        .expectOne(`${environment.apiUrl}/auth/register`)
        .flush(
          { id: 1, username: 'ana', email: 'ana@example.com', createdAt: new Date().toISOString() },
          { status: 201, statusText: 'Created' },
        );
      await fixture.whenStable();
      fixture.detectChanges();
      const target = returnUrl ? '/auth/login?returnUrl=%2Flibrary%3Ftab%3Dplaying' : '/auth/login';
      expect(navigate).toHaveBeenCalledWith(target, { replaceUrl: true });
      expect(storage.read()).toBeNull();
    },
  );

  it('stays in registration when account creation fails', async () => {
    await render('register');
    input('email', 'ana@example.com');
    input('username', 'ana');
    input('password', 'Str0ngPass123');
    submit();
    http
      .expectOne(`${environment.apiUrl}/auth/register`)
      .flush({ title: 'Email already registered' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(navigate).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'El email ya está registrado.',
    );
  });

  it.each(['register'])('redirects an active session away from %s', async (mode) => {
    storage.save(session());
    await render(mode);
    expect(navigate).toHaveBeenCalledWith('/library', { replaceUrl: true });
  });
});
