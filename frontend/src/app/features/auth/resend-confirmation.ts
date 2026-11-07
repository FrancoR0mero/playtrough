import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { AuthService } from './services/auth';
import { createResendCooldown } from './resend-cooldown';
import { createRateLimitCooldown } from './rate-limit-cooldown';

@Component({
  selector: 'app-resend-confirmation',
  imports: [ReactiveFormsModule],
  templateUrl: './resend-confirmation.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResendConfirmationComponent {
  readonly email = input('');
  readonly editable = input(true);
  readonly buttonLabel = input('Reenviar correo de confirmación');
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly cooldown = createResendCooldown(60);
  protected readonly rateLimit = createRateLimitCooldown();
  protected readonly remaining = this.cooldown.remaining;
  protected readonly submitting = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  constructor() {
    effect(() => this.form.controls.email.setValue(this.email().trim()));
  }

  protected emailError(): string | null {
    const control = this.form.controls.email;
    if (!control.touched) return null;
    if (control.hasError('required')) return 'El email es obligatorio.';
    return control.hasError('email') ? 'El email no es válido.' : null;
  }

  protected submit(): void {
    if (this.submitting() || this.remaining() > 0 || this.rateLimit.remaining() > 0) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      this.element.nativeElement.querySelector<HTMLInputElement>('input')?.focus();
      return;
    }
    this.submitting.set(true);
    this.error.set(null);
    this.sent.set(false);
    this.auth
      .resendConfirmation(this.form.getRawValue().email)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => {
          this.sent.set(true);
          this.cooldown.start();
        },
        error: (error: unknown) => {
          if (!this.rateLimit.handle(error))
            this.error.set('No pudimos procesar la solicitud. Intentá de nuevo.');
        },
      });
  }
}
