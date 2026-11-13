import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize, map } from 'rxjs';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from './profile-error';
import { UsersService } from './services/users';

@Component({
  selector: 'app-avatar-editor',
  imports: [UserAvatarComponent],
  templateUrl: './avatar-editor.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AvatarEditorComponent {
  readonly username = input.required<string>();
  readonly avatarUrl = input<string | null>(null);
  readonly disabled = input(false);
  readonly changed = output<string | null>();
  readonly busyChanged = output<boolean>();
  private readonly users = inject(UsersService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly selected = signal<File | null>(null);
  protected readonly preview = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly confirming = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly status = signal('');
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    this.destroyRef.onDestroy(() => this.clearSelection());
  }

  protected select(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.disabled() || this.saving()) return;
    this.clearSelection();
    this.error.set(null);
    this.status.set('');
    this.confirming.set(false);
    if (file.size > 5 * 1024 * 1024) {
      this.error.set('La foto debe pesar hasta 5 MB.');
      return;
    }
    if (
      !file.size ||
      !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
      !/\.(jpe?g|png|webp)$/i.test(file.name)
    ) {
      this.error.set('Elegí una imagen JPG, PNG o WebP.');
      return;
    }
    this.selected.set(file);
    this.preview.set(URL.createObjectURL(file));
  }

  protected clearSelection(): void {
    const url = this.preview();
    if (url) URL.revokeObjectURL(url);
    this.preview.set(null);
    this.selected.set(null);
  }

  protected previewFailed(): void {
    this.clearSelection();
    this.error.set('No pudimos leer esa imagen. Elegí otro archivo.');
  }

  protected save(remove = false): void {
    if (
      this.disabled() ||
      this.saving() ||
      this.cooldown.remaining() ||
      (!remove && !this.selected()) ||
      (remove && !this.confirming())
    )
      return;
    this.saving.set(true);
    this.busyChanged.emit(true);
    this.error.set(null);
    this.status.set('');
    const operation: Observable<string | null> = remove
      ? this.users.deleteAvatar().pipe(map(() => null))
      : this.users.uploadAvatar(this.selected()!).pipe(map((result) => result.avatarUrl));
    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => {
          this.saving.set(false);
          this.busyChanged.emit(false);
        }),
      )
      .subscribe({
        next: (url) => {
          this.changed.emit(url);
          this.clearSelection();
          this.confirming.set(false);
          this.status.set(
            remove ? 'Foto eliminada. Volvés a usar tus iniciales.' : 'Foto actualizada',
          );
        },
        error: (error) => {
          if (this.cooldown.handle(error)) return;
          const messages: Record<string, string> = {
            invalid_avatar: 'Elegí una imagen JPG, PNG o WebP válida, sin animación.',
            invalid_avatar_dimensions:
              'La imagen debe tener entre 64 y 4096 píxeles por lado y hasta 16 megapíxeles.',
            avatar_too_large: 'La foto debe pesar hasta 5 MB.',
            payload_too_large: 'La foto debe pesar hasta 5 MB.',
            concurrent_modification: 'Tu perfil cambió en otra sesión. Volvé a intentar.',
          };
          this.error.set(
            messages[profileError(error).code ?? ''] ??
              'No pudimos guardar la foto. Revisá tu conexión e intentá de nuevo.',
          );
        },
      });
  }
}
