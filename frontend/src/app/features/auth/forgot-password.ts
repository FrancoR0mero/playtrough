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
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from './services/auth';
import { createResendCooldown } from './resend-cooldown';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly cooldown = createResendCooldown(30);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });
  protected readonly submitting = signal(false);
  protected readonly sent = signal(false);
  protected readonly remaining = this.cooldown.remaining;
  protected readonly error = signal<string | null>(null);

  protected emailError(): string | null {
    const field = this.form.controls.email;
    if (!field.touched) return null;
    if (field.hasError('required')) return 'El email es obligatorio.';
    return field.hasError('email') ? 'El email no es válido.' : null;
  }

  protected submit(): void {
    if (this.submitting() || this.remaining() > 0) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.element.nativeElement.querySelector<HTMLInputElement>('input')?.focus();
      return;
    }
    this.error.set(null);
    this.submitting.set(true);
    this.auth
      .forgotPassword(this.form.getRawValue().email)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => {
          this.sent.set(true);
          this.cooldown.start();
        },
        error: () => this.error.set('No pudimos procesar la solicitud. Intentá de nuevo.'),
      });
  }
}
