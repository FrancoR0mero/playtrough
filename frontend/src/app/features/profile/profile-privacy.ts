import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { PrivacyDto, Visibility } from './models/user';
import { profileError } from './profile-error';
import { UsersService } from './services/users';

@Component({
  selector: 'app-profile-privacy',
  imports: [ReactiveFormsModule],
  templateUrl: './profile-privacy.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePrivacyComponent {
  private readonly users = inject(UsersService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly saved = signal<PrivacyDto | null>(null);
  protected readonly form = new FormGroup({
    profileVisibility: new FormControl<Visibility>('public', { nonNullable: true }),
    historyVisibility: new FormControl<Visibility>('public', { nonNullable: true }),
  });
  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.value });
  protected readonly changed = computed(
    () =>
      !!this.saved() &&
      (this.saved()!.profileVisibility !== this.value().profileVisibility ||
        this.saved()!.historyVisibility !== this.value().historyVisibility),
  );
  protected readonly loaded = signal(false);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal<string | null>(null);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly groups = [
    {
      key: 'profileVisibility' as const,
      label: 'Perfil',
      description:
        'Elegí quién puede ver tu biografía, fecha de alta y amigos. Tu nombre y avatar seguirán visibles.',
    },
    {
      key: 'historyVisibility' as const,
      label: 'Biblioteca e historial',
      description:
        'Elegí quién puede ver tu biblioteca y el estado de tus juegos. Esta opción es independiente de la privacidad del perfil.',
    },
  ];
  protected readonly options = [
    {
      value: 'public' as const,
      label: 'Público',
      description: 'Cualquier jugador con sesión iniciada.',
    },
    {
      value: 'friends' as const,
      label: 'Solo amigos',
      description: 'Vos y las personas que sean tus amigas.',
    },
    { value: 'private' as const, label: 'Privado', description: 'Solo vos.' },
  ];

  constructor() {
    this.load();
  }

  protected load(): void {
    if (this.loading() || this.cooldown.remaining()) return;
    this.loading.set(true);
    this.error.set(null);
    this.users
      .getPrivacy()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (value) => {
          this.saved.set(value);
          this.form.setValue(value);
          this.loaded.set(true);
          this.loading.set(false);
        },
        error: (error) => {
          this.loading.set(false);
          this.handleError(error, false);
        },
      });
  }

  protected save(): void {
    if (!this.loaded() || !this.changed() || this.saving() || this.cooldown.remaining()) return;
    this.saving.set(true);
    this.error.set(null);
    this.status.set(null);
    this.users
      .updatePrivacy(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (value) => {
          this.saved.set(value);
          this.form.setValue(value);
          this.saving.set(false);
          this.status.set('Privacidad actualizada');
        },
        error: (error) => {
          this.saving.set(false);
          this.handleError(error, true);
        },
      });
  }

  protected keyboard(event: KeyboardEvent, key: keyof PrivacyDto): void {
    if (this.saving()) return;
    const keys = ['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp', 'Home', 'End'];
    if (!keys.includes(event.key)) return;
    event.preventDefault();
    const index = this.options.findIndex(
      (option) => option.value === this.form.controls[key].value,
    );
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? 2
          : (index + (event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : 2)) % 3;
    this.form.controls[key].setValue(this.options[next].value);
    this.form.controls[key].markAsDirty();
    (event.currentTarget as HTMLElement).querySelectorAll<HTMLInputElement>('input')[next]?.focus();
    this.status.set(null);
  }

  private handleError(error: unknown, saving: boolean): void {
    if (this.cooldown.handle(error)) {
      this.error.set('Podés reintentar cuando termine la espera.');
      return;
    }
    this.error.set(
      profileError(error).code === 'invalid_visibility'
        ? 'Elegí una opción válida para el perfil y el historial.'
        : saving
          ? 'No pudimos guardar la privacidad. Revisá tu conexión e intentá de nuevo.'
          : 'No pudimos cargar la privacidad. Revisá tu conexión y reintentá.',
    );
  }
}
