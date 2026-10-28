import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  Input,
  OnChanges,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { ProblemDetails } from '../auth/models/register';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { displayLibraryDate, localCalendarDate } from './library-date';
import { LIBRARY_STATUSES, LibraryEntryDto, LibraryStatus } from './models/library';
import { LibraryService } from './services/library';

@Component({
  selector: 'app-library-entry',
  imports: [ReactiveFormsModule],
  templateUrl: './library-entry.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryEntryComponent implements OnChanges {
  @Input({ required: true }) gameId!: number;
  @Input({ required: true }) gameName = '';
  private readonly library = inject(LibraryService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private read?: Subscription;
  private mutation?: Subscription;
  protected readonly states = LIBRARY_STATUSES;
  protected readonly entry = signal<LibraryEntryDto | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal('');
  protected readonly confirmRemoval = signal(false);
  protected readonly showDate = signal(false);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly blocked = computed(
    () => this.loading() || this.saving() || this.cooldown.remaining() > 0,
  );
  protected readonly currentState = computed(() =>
    this.states.find((state) => state.value === this.entry()?.status),
  );
  protected readonly status = new FormControl<LibraryStatus | null>(null);
  protected readonly date = new FormControl(localCalendarDate(), { nonNullable: true });
  protected readonly formatDate = displayLibraryDate;
  protected today = localCalendarDate();

  ngOnChanges(): void {
    this.read?.unsubscribe();
    this.mutation?.unsubscribe();
    this.entry.set(null);
    this.loaded.set(false);
    this.error.set(null);
    this.notice.set('');
    this.confirmRemoval.set(false);
    this.resetForm();
    this.load();
  }

  protected load(preserveError = false): void {
    if (this.loading() || this.saving()) return;
    if (this.cooldown.remaining()) {
      this.error.set('Ya podés reintentar la carga de tu biblioteca.');
      return;
    }
    this.loading.set(true);
    if (!preserveError) this.error.set(null);
    this.read = this.library
      .getEntry(this.gameId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (entry) => {
          this.entry.set(entry);
          this.loaded.set(true);
          this.resetForm();
        },
        error: (error: unknown) => {
          if (
            this.code(error) === 'not_in_library' &&
            error instanceof HttpErrorResponse &&
            error.status === 404
          ) {
            this.entry.set(null);
            this.loaded.set(true);
            this.resetForm();
          } else {
            this.loaded.set(false);
            this.handleError(error, false);
          }
        },
      });
  }

  protected toggleDate(): void {
    if (this.blocked()) return;
    this.showDate.update((value) => !value);
    if (this.showDate()) {
      this.today = localCalendarDate();
      this.date.setValue(this.today);
    }
  }

  protected selectStatus(value: LibraryStatus): void {
    if (!this.blocked()) this.status.setValue(value);
  }

  protected moveStatus(event: KeyboardEvent, index: number): void {
    const keys = ['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp', 'Home', 'End'];
    if (!keys.includes(event.key) || this.blocked()) return;
    event.preventDefault();
    const count = this.states.length;
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? count - 1
          : (index + (event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : -1) + count) %
            count;
    this.selectStatus(this.states[next].value);
    this.element.nativeElement.querySelectorAll<HTMLButtonElement>('[role="radio"]')[next]?.focus();
  }

  protected save(): void {
    if (this.blocked() || !this.loaded() || !this.status.value) return;
    this.notice.set('');
    this.today = localCalendarDate();
    if (
      this.showDate() &&
      (!/^\d{4}-\d{2}-\d{2}$/.test(this.date.value) || this.date.value > this.today)
    ) {
      this.error.set('Revisá la fecha');
      return;
    }
    const existing = !!this.entry();
    const occurredOn = this.showDate() ? this.date.value : undefined;
    this.saving.set(true);
    this.error.set(null);
    const request = existing
      ? this.library.changeStatus(this.gameId, this.status.value, occurredOn)
      : this.library.add(this.gameId, this.status.value, occurredOn);
    this.mutation = request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (entry) => {
          this.entry.set(entry);
          this.resetForm();
          this.notice.set(existing ? 'Estado actualizado' : 'Agregado a tu biblioteca');
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.handleError(error, true);
        },
      });
  }

  protected askRemoval(): void {
    if (this.blocked()) return;
    this.confirmRemoval.set(true);
    this.focus('cancel-remove');
  }

  protected cancelRemoval(): void {
    if (this.blocked()) return;
    this.confirmRemoval.set(false);
    this.focus('remove-library');
  }

  protected remove(): void {
    if (this.blocked() || !this.confirmRemoval() || !this.entry()) return;
    this.saving.set(true);
    this.error.set(null);
    this.notice.set('');
    this.mutation = this.library
      .remove(this.gameId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.entry.set(null);
          this.confirmRemoval.set(false);
          this.resetForm();
          this.notice.set('Quitado de tu biblioteca');
          this.focus('choose-playing');
        },
        error: (error: unknown) => {
          this.saving.set(false);
          this.handleError(error, true);
        },
      });
  }

  private resetForm(): void {
    this.status.setValue(this.entry()?.status ?? null);
    this.showDate.set(false);
    this.today = localCalendarDate();
    this.date.setValue(this.today);
  }

  private code(error: unknown): string | undefined {
    const problem =
      error instanceof HttpErrorResponse ? (error.error as ProblemDetails | null) : null;
    return problem?.extensions?.code ?? problem?.code;
  }

  private handleError(error: unknown, mutation: boolean): void {
    if (error instanceof HttpErrorResponse && error.status === 401) return;
    if (this.cooldown.handle(error)) {
      this.error.set('Ya podés reintentar la operación en tu biblioteca.');
      return;
    }
    const code = this.code(error);
    const problem =
      error instanceof HttpErrorResponse
        ? (error.error as { extensions?: { minDate?: string }; minDate?: string } | null)
        : null;
    const minDate = problem?.extensions?.minDate ?? problem?.minDate;
    const messages: Record<string, string> = {
      already_in_library: 'Este juego ya está en tu biblioteca. Actualizamos su estado.',
      not_in_library: 'Este juego ya no está en tu biblioteca.',
      invalid_status: 'Elegí un estado válido.',
      invalid_date: 'Revisá la fecha',
      date_before_previous_event: `La fecha no puede ser anterior al último cambio${minDate ? ` (${displayLibraryDate(minDate)})` : ''}`,
      game_not_found: 'No encontramos este juego.',
      concurrent_modification:
        'Tu biblioteca cambió en otra sesión. Revisá el estado actualizado y volvé a intentar.',
    };
    this.error.set(
      messages[code ?? ''] ??
        `No pudimos ${mutation ? 'actualizar' : 'cargar'} tu biblioteca. Revisá tu conexión e intentá de nuevo.`,
    );
    if (!mutation) return;
    if (code === 'not_in_library') {
      this.entry.set(null);
      this.confirmRemoval.set(false);
      this.resetForm();
      this.focus('choose-playing');
    } else if (code === 'already_in_library' || code === 'concurrent_modification') {
      this.confirmRemoval.set(false);
      this.loaded.set(false);
      this.load(true);
    }
  }

  private focus(action: string): void {
    afterNextRender(
      () =>
        this.element.nativeElement
          .querySelector<HTMLButtonElement>(`[data-action="${action}"]`)
          ?.focus(),
      { injector: this.injector },
    );
  }
}
