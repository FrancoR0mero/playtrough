import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { NotificationPreference, NotificationsApi } from './notifications-api';

const categoryLabels: Record<string, { title: string; description: string }> = {
  friends: { title: 'Amistades', description: 'Solicitudes de amistad y aceptaciones.' },
  interactions: {
    title: 'Interacciones',
    description: 'Me gusta y comentarios en tus publicaciones.',
  },
  groups: { title: 'Grupos', description: 'Solicitudes de ingreso y cambios de membresía.' },
  account: { title: 'Cuenta', description: 'Cambios importantes de tu cuenta y suscripción.' },
  moderation: { title: 'Moderación', description: 'Decisiones y respuestas a tus apelaciones.' },
};

@Component({
  selector: 'app-notification-preferences',
  imports: [RouterLink],
  templateUrl: './notification-preferences.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationPreferencesComponent implements OnInit {
  private readonly api = inject(NotificationsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly items = signal<NotificationPreference[]>([]);
  protected readonly optional = computed(() => this.items().filter((option) => option.editable));
  protected readonly essential = computed(() => this.items().filter((option) => !option.editable));
  protected readonly loading = signal(false);
  protected readonly loadError = signal(false);
  protected readonly saveError = signal(false);
  protected readonly saving = signal(false);
  protected readonly feedback = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected label(category: string) {
    return categoryLabels[category] ?? { title: category, description: '' };
  }

  protected load(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.loadError.set(false);
    this.api
      .preferences()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (preferences) => this.items.set(preferences),
        error: () => this.loadError.set(true),
      });
  }

  protected change(option: NotificationPreference, input: HTMLInputElement): void {
    const enabled = input.checked;
    if (!option.editable || this.saving() || option.enabled === enabled) {
      input.checked = option.enabled;
      return;
    }
    this.saving.set(true);
    this.saveError.set(false);
    this.feedback.set('');
    this.replace({ ...option, enabled });
    this.api
      .setPreference(option.category, enabled)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (saved) => {
          this.replace(saved);
          this.feedback.set(
            `Avisos de ${this.label(saved.category).title.toLowerCase()} ${saved.enabled ? 'activados' : 'desactivados'}.`,
          );
        },
        error: () => {
          input.checked = option.enabled;
          this.replace(option);
          this.saveError.set(true);
        },
      });
  }

  private replace(preference: NotificationPreference): void {
    this.items.update((items) =>
      items.map((item) => (item.category === preference.category ? preference : item)),
    );
  }
}
