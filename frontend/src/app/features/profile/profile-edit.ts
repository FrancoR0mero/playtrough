import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { catchError, map, of, switchMap } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { AuthService } from '../auth/services/auth';
import { normalizeReviewText, reviewTextLength } from '../reviews/review-text';
import { MyProfileDto, UpdateProfileRequest } from './models/user';
import { profileDate, profileError } from './profile-error';
import { UsersService } from './services/users';

export interface ProfileEditResult {
  profile: MyProfileDto;
  warning: string | null;
  renamed: boolean;
}

@Component({
  selector: 'app-profile-edit',
  imports: [ReactiveFormsModule],
  templateUrl: './profile-edit.html',
  styles: ':host { display:block; max-height:90dvh; overflow-y:auto; }',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileEditComponent {
  protected readonly initial = inject<MyProfileDto>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<ProfileEditResult>>(DialogRef);
  private readonly users = inject(UsersService);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly availableAt = signal(this.initial.usernameChangeAvailableAt);
  protected readonly date = profileDate;
  protected readonly form = new FormGroup({
    username: new FormControl(
      { value: this.initial.username, disabled: this.isBlocked(this.availableAt()) },
      {
        nonNullable: true,
        validators: [Validators.required, Validators.pattern(/^[A-Za-z0-9_]{3,20}$/)],
      },
    ),
    bio: new FormControl(this.initial.bio ?? '', {
      nonNullable: true,
      validators: [
        (control) => (reviewTextLength(control.value) > 300 ? { maxlength: true } : null),
      ],
    }),
  });
  private readonly values = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  protected readonly bioLength = computed(() => reviewTextLength(this.values().bio ?? ''));
  protected readonly changed = computed(() => {
    const value = this.values();
    return (
      (value.username !== undefined &&
        value.username.toLowerCase() !== this.initial.username.toLowerCase()) ||
      normalizeReviewText(value.bio ?? '') !== (this.initial.bio ?? '')
    );
  });

  private isBlocked(date: string | null): boolean {
    return !!date && Date.parse(date) > Date.now();
  }

  protected save(): void {
    if (this.saving() || this.form.invalid || !this.changed() || this.cooldown.remaining()) return;
    const value = this.form.getRawValue();
    const payload: UpdateProfileRequest = {};
    if (
      this.form.controls.username.enabled &&
      value.username.toLowerCase() !== this.initial.username.toLowerCase()
    )
      payload.username = value.username;
    const bio = normalizeReviewText(value.bio) || null;
    if (bio !== this.initial.bio) payload.bio = bio;
    this.saving.set(true);
    this.error.set(null);
    this.dialog.disableClose = true;
    this.users
      .updateMe(payload)
      .pipe(
        switchMap((profile) => {
          const renamed = profile.username.toLowerCase() !== this.initial.username.toLowerCase();
          if (!renamed) return of({ profile, warning: null, renamed });
          this.auth.updateCurrentUsername(profile.id, profile.username);
          return this.auth.refresh({ redirectOnFailure: false }).pipe(
            map(() => {
              this.auth.updateCurrentUsername(profile.id, profile.username);
              return { profile, warning: null, renamed };
            }),
            catchError(() =>
              of({
                profile,
                renamed,
                warning:
                  'Tu perfil se guardó, pero no pudimos renovar la sesión. El nombre puede tardar en actualizarse.',
              }),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (result) => this.dialog.close(result),
        error: (error) => {
          this.saving.set(false);
          this.dialog.disableClose = false;
          if (this.cooldown.handle(error)) return;
          const problem = profileError(error);
          const messages: Record<string, string> = {
            username_taken: 'Ese nombre ya está en uso',
            username_reserved: 'Ese nombre está reservado. Elegí otro.',
            invalid_username: 'Usá entre 3 y 20 letras, números o guiones bajos.',
            invalid_bio: 'La biografía debe tener hasta 300 caracteres.',
            username_change_too_soon: 'Todavía no podés cambiar tu nombre de usuario.',
          };
          if (problem.code === 'username_change_too_soon' && problem.availableAt) {
            this.availableAt.set(problem.availableAt);
            if (this.isBlocked(problem.availableAt)) this.form.controls.username.disable();
          }
          this.error.set(
            messages[problem.code ?? ''] ??
              'No pudimos guardar el perfil. Revisá tu conexión e intentá de nuevo.',
          );
        },
      });
  }
}
