import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AbstractControl,
  FormBuilder,
  FormControl,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import type { RegisterRequest } from './models/register';
import { AuthService, LoginError, RegisterError } from './services/auth';
import { authDestination } from './auth-navigation';
import { passwordComplexityValidator } from './password-validation';
import { ResendConfirmationComponent } from './resend-confirmation';
import { createRateLimitCooldown } from './rate-limit-cooldown';

type RegisterField = 'email' | 'username' | 'password';

/** Charset mirrors the backend regex; length is enforced separately by the min/max validators. */
const USERNAME_PATTERN = /^[a-zA-Z0-9_.]+$/;

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, ResendConfirmationComponent],
  templateUrl: './register.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly routeData = toSignal(this.route.data, {
    initialValue: this.route.snapshot.data,
  });

  protected readonly isLogin = computed(() => this.routeData()['mode'] === 'login');
  private readonly loginRateLimit = createRateLimitCooldown();
  private readonly registerRateLimit = createRateLimitCooldown();
  protected readonly rateLimit = computed(() =>
    this.isLogin() ? this.loginRateLimit : this.registerRateLimit,
  );
  protected readonly showPassword = signal(false);
  protected readonly registeredEmail = signal<string | null>(null);
  protected readonly needsConfirmation = signal(false);
  protected readonly confirmationEmail = signal('');
  private readonly queryParams = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  protected readonly returnUrl = computed(() => this.queryParams().get('returnUrl'));
  private readonly expirationNoticeDismissed = signal(false);
  protected readonly sessionExpired = computed(
    () =>
      this.isLogin() &&
      this.queryParams().get('sessionExpired') === 'true' &&
      !this.expirationNoticeDismissed(),
  );

  protected readonly passwordUpdated = computed(
    () =>
      this.isLogin() &&
      this.queryParams().get('passwordUpdated') === 'true' &&
      !this.expirationNoticeDismissed(),
  );

  ngOnInit(): void {
    this.auth.restoreSession();
    if (this.auth.isAuthenticated()) this.redirectAfterLogin();
  }

  private redirectAfterLogin(): void {
    void this.router.navigateByUrl(authDestination(this.router, this.returnUrl()), {
      replaceUrl: true,
    });
  }

  protected readonly submitting = signal(false);
  protected readonly submitAttempted = signal(false);
  protected readonly fieldErrors = signal<Record<string, string>>({});
  protected readonly formError = signal<string | null>(null);

  protected readonly loginForm = this.formBuilder.nonNullable.group({
    identifier: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected loginFieldError(field: 'identifier' | 'password'): string | null {
    const control = this.loginForm.controls[field];
    if (!control.touched) return null;
    return (
      this.fieldErrors()[field] ??
      (control.hasError('required')
        ? field === 'identifier'
          ? 'El email o nombre de usuario es obligatorio.'
          : 'La contraseña es obligatoria.'
        : null)
    );
  }

  protected clearFieldError(field: string): void {
    if (field === 'identifier') this.needsConfirmation.set(false);
    this.fieldErrors.update((errors) => {
      const updated = { ...errors };
      delete updated[field];
      return updated;
    });
    this.formError.set(null);
  }

  private focusFirstError(): void {
    const fields = this.isLogin() ? ['identifier', 'password'] : ['email', 'username', 'password'];
    const form: AbstractControl = this.isLogin() ? this.loginForm : this.form;
    const field = fields.find((name) => form.get(name)?.invalid || this.fieldErrors()[name]);
    if (field)
      this.element.nativeElement
        .querySelector<HTMLInputElement>(`[formControlName="${field}"]`)
        ?.focus();
  }

  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    username: [
      '',
      [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(20),
        Validators.pattern(USERNAME_PATTERN),
      ],
    ],
    password: ['', [Validators.required, Validators.minLength(8), passwordComplexityValidator]],
  });

  protected onSubmit(): void {
    if (this.submitting() || this.rateLimit().remaining() > 0) return;
    if (!this.isLogin() && this.registeredEmail()) return;
    this.expirationNoticeDismissed.set(true);
    if (this.isLogin()) {
      this.submitLogin();
      return;
    }
    this.submitAttempted.set(true);
    this.form.markAllAsTouched();
    this.fieldErrors.set({});
    this.formError.set(null);

    if (this.form.invalid || this.submitting()) {
      this.focusFirstError();
      return;
    }

    this.submitting.set(true);
    const payload: RegisterRequest = this.form.getRawValue();

    this.auth
      .register(payload)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          this.submitting.set(false);
          this.registeredEmail.set(response.email);
          this.form.controls.password.reset();
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          if (this.registerRateLimit.handle(error)) return;

          if (error instanceof RegisterError) {
            this.fieldErrors.set(error.fieldErrors);
            this.formError.set(error.formError);
            this.focusFirstError();
            return;
          }

          this.formError.set('Ocurrió un error inesperado. Inténtalo de nuevo.');
        },
      });
  }

  private submitLogin(): void {
    this.loginForm.markAllAsTouched();
    this.fieldErrors.set({});
    this.formError.set(null);
    if (this.loginForm.invalid) {
      this.focusFirstError();
      return;
    }
    this.submitting.set(true);
    const { identifier, password } = this.loginForm.getRawValue();
    this.auth
      .login(identifier, password)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => this.redirectAfterLogin(),
        error: (error: unknown) => {
          if (this.loginRateLimit.handle(error)) {
            this.needsConfirmation.set(false);
            return;
          }
          if (error instanceof LoginError) {
            this.needsConfirmation.set(error.code === 'email_not_confirmed');
            const email = identifier.trim();
            this.confirmationEmail.set(
              email && !Validators.email(new FormControl(email)) ? email : '',
            );
            this.fieldErrors.set(error.fieldErrors);
            this.formError.set(error.formError);
            this.focusFirstError();
          } else {
            this.needsConfirmation.set(false);
            this.formError.set('Ocurrió un error inesperado. Inténtalo de nuevo.');
          }
        },
      });
  }

  protected fieldError(field: RegisterField): string | null {
    const serverError = this.fieldErrors()[field];
    if (serverError) {
      return serverError;
    }

    const control = this.form.controls[field];
    if (!control.invalid || (!control.touched && !this.submitAttempted())) {
      return null;
    }

    return this.describeError(field, control.errors ?? {});
  }

  private describeError(field: RegisterField, errors: ValidationErrors): string | null {
    if (errors['required']) {
      if (field === 'email') {
        return 'El email es obligatorio.';
      }
      if (field === 'username') {
        return 'El nombre de usuario es obligatorio.';
      }
      return 'La contraseña es obligatoria.';
    }

    if (field === 'email' && errors['email']) {
      return 'El email no es válido.';
    }

    if (field === 'username' && (errors['minlength'] || errors['maxlength'])) {
      return 'El nombre de usuario debe tener entre 3 y 20 caracteres.';
    }

    if (field === 'username' && errors['pattern']) {
      return 'El nombre de usuario solo puede contener letras, números, guion bajo y punto.';
    }

    if (field === 'password' && errors['minlength']) {
      return 'La contraseña debe tener al menos 8 caracteres.';
    }

    if (field === 'password' && errors['uppercase']) {
      return 'Debe incluir al menos una mayúscula.';
    }

    if (field === 'password' && errors['lowercase']) {
      return 'Debe incluir al menos una minúscula.';
    }

    if (field === 'password' && errors['digit']) {
      return 'Debe incluir al menos un número.';
    }

    return null;
  }
}
