import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Mirrors the ASP.NET Identity password policy: 8+ chars, uppercase, lowercase and digit (symbol NOT required). */
export const passwordComplexityValidator: ValidatorFn = (
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
