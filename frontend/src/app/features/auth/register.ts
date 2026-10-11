import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  ValidatorFn,
  Validators,
} from '@angular/forms';

type RegisterField = 'email' | 'username' | 'password';

/** Charset mirrors the backend regex; length is enforced separately by the min/max validators. */
const USERNAME_PATTERN = /^[a-zA-Z0-9_.]+$/;

/** Mirrors the ASP.NET Identity password policy: 8+ chars, uppercase, lowercase and digit (symbol NOT required). */
const passwordComplexityValidator: ValidatorFn = (
  control: AbstractControl,
): ValidationErrors | null => {
  const value = String(control.value ?? '');
  if (value.length === 0) {
    return null;
  }

  const errors: ValidationErrors = {};
  if (!/[A-Z]/.test(value)) {
    errors['uppercase'] = true;
  }
  if (!/[a-z]/.test(value)) {
    errors['lowercase'] = true;
  }
  if (!/\d/.test(value)) {
    errors['digit'] = true;
  }

  return Object.keys(errors).length > 0 ? errors : null;
};

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule],
  templateUrl: './register.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegisterComponent {
  private readonly formBuilder = inject(FormBuilder);

  protected readonly submitAttempted = signal(false);

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
    this.submitAttempted.set(true);
    this.form.markAllAsTouched();

    if (this.form.invalid) {
      return;
    }
  }

  protected fieldError(field: RegisterField): string | null {
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
