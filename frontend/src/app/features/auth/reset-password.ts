import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { passwordComplexityValidator } from './password-validation';
import { AuthService } from './services/auth';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  // Keep link credentials only in this component instance; never log or persist them.
  private readonly email = this.route.snapshot.queryParamMap.get('email') ?? '';
  private readonly token = this.route.snapshot.queryParamMap.get('token') ?? '';
  protected readonly invalidLink = signal(!this.email.trim() || !this.token.trim());
  protected readonly submitting = signal(false);
  protected readonly showPassword = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly serverPasswordError = signal<string | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: [
        '',
        [Validators.required, Validators.minLength(8), passwordComplexityValidator],
      ],
      confirmPassword: ['', Validators.required],
    },
    {
      validators: (control) =>
        control.get('newPassword')?.value === control.get('confirmPassword')?.value
          ? null
          : { mismatch: true },
    },
  );

  constructor() {
    // Replacing this history entry prevents Back from exposing the email/reset token.
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
  }

  protected fieldError(field: 'newPassword' | 'confirmPassword'): string | null {
    const control = this.form.controls[field];
    if (!control.touched) return null;
    if (control.hasError('required')) return 'La contraseña es obligatoria.';
    if (field === 'confirmPassword')
      return this.form.hasError('mismatch') ? 'Las contraseñas no coinciden.' : null;
    if (this.serverPasswordError()) return this.serverPasswordError();
    if (control.hasError('minlength')) return 'La contraseña debe tener al menos 8 caracteres.';
    if (control.hasError('uppercase')) return 'Debe incluir al menos una mayúscula.';
    if (control.hasError('lowercase')) return 'Debe incluir al menos una minúscula.';
    return control.hasError('digit') ? 'Debe incluir al menos un número.' : null;
  }

  private focusFirstError(): void {
    const field =
      this.form.controls.newPassword.invalid || this.serverPasswordError()
        ? 'newPassword'
        : 'confirmPassword';
    this.element.nativeElement
      .querySelector<HTMLInputElement>(`[formControlName="${field}"]`)
      ?.focus();
  }

  protected submit(): void {
    if (this.submitting() || this.invalidLink()) return;
    this.form.markAllAsTouched();
    this.serverPasswordError.set(null);
    this.error.set(null);
    if (this.form.invalid) {
      this.focusFirstError();
      return;
    }
    this.submitting.set(true);
    const { newPassword, confirmPassword } = this.form.getRawValue();
    this.auth
      .resetPassword(this.email, this.token, newPassword, confirmPassword)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => {
          this.auth.clearSession();
          this.form.reset();
          void this.router.navigateByUrl('/auth/login?passwordUpdated=true', { replaceUrl: true });
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 400) {
            const errors: unknown = error.error?.errors;
            const messages =
              errors && typeof errors === 'object'
                ? Object.entries(errors)
                    .filter(([key]) => key.toLowerCase() === 'newpassword')
                    .flatMap(([, value]) =>
                      Array.isArray(value)
                        ? value.filter((item): item is string => typeof item === 'string')
                        : [],
                    )
                : [];
            if (messages.length) {
              this.serverPasswordError.set(messages.join(' '));
              this.focusFirstError();
            } else this.invalidLink.set(true);
          } else this.error.set('No pudimos cambiar la contraseña. Intentá de nuevo.');
        },
      });
  }
}
