import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { BlocksService } from './services/blocks';

@Component({
  selector: 'app-block-user-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (!confirming()) {
      <button
        #blockButton
        type="button"
        data-action="block-user"
        (click)="confirm()"
        class="ui-button min-h-11 border border-slate-600 px-4 py-2 text-red-200"
      >
        Bloquear usuario
      </button>
    } @else {
      <div
        role="group"
        aria-label="Confirmar bloqueo"
        class="mt-4 max-w-2xl border-t border-white/10 pt-5"
      >
        <h2 class="font-semibold text-white">¿Bloquear a {{ username() }}?</h2>
        <p class="mt-2 text-sm leading-relaxed text-slate-300">
          Se eliminará la amistad y se cancelarán las solicitudes pendientes. No podrán ver sus
          perfiles ni bibliotecas ni enviarse nuevas solicitudes. No se enviará una notificación.
        </p>
        <div class="mt-4 flex flex-wrap gap-3">
          <button
            #confirmButton
            type="button"
            data-action="confirm-block"
            (click)="block()"
            [disabled]="busy() || cooldown.remaining() > 0"
            class="ui-button min-h-11 border border-red-400 px-4 py-2 text-red-200 disabled:opacity-50"
          >
            {{ busy() ? 'Bloqueando…' : 'Sí, bloquear' }}
          </button>
          <button
            type="button"
            (click)="cancel()"
            [disabled]="busy()"
            class="ui-button min-h-11 border border-slate-600 px-4 py-2 text-white"
          >
            Cancelar bloqueo
          </button>
        </div>
      </div>
    }
    @if (error() || cooldown.message(); as message) {
      <p role="alert" class="mt-3 text-sm text-red-300">{{ cooldown.message() || message }}</p>
    }
  `,
})
export class BlockUserButtonComponent {
  readonly username = input.required<string>();
  readonly blocked = output<void>();
  private readonly blocks = inject(BlocksService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly cooldown = createRateLimitCooldown();
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('blockButton');
  private readonly confirmation = viewChild<ElementRef<HTMLButtonElement>>('confirmButton');
  protected confirm(): void {
    this.confirming.set(true);
    afterNextRender(
      () => {
        const button = this.confirmation()?.nativeElement;
        button?.scrollIntoView?.({ block: 'center' });
        button?.focus({ preventScroll: true });
      },
      { injector: this.injector },
    );
  }
  protected cancel(): void {
    this.confirming.set(false);
    this.error.set(null);
    afterNextRender(() => this.trigger()?.nativeElement.focus(), { injector: this.injector });
  }
  protected block(): void {
    if (!this.confirming() || this.busy() || this.cooldown.remaining()) return;
    this.busy.set(true);
    this.error.set(null);
    this.blocks
      .block(this.username())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: () => this.blocked.emit(),
        error: (error) => {
          if (!this.cooldown.handle(error))
            this.error.set(
              'No pudimos bloquear a este usuario. Revisá tu conexión e intentá de nuevo.',
            );
        },
      });
  }
}
