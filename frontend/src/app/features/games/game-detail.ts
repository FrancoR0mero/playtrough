import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { RawgAttributionComponent } from '../../shared/components/layout/rawg-attribution';
import { ProblemDetails } from '../auth/models/register';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { LibraryEntryComponent } from '../library/library-entry';
import { GameImageComponent } from './game-image';
import { GameDetail, GameSummary } from './models/game';
import { GameCatalogService } from './services/game-catalog';

export const GAME_SUMMARY_PREVIEW_LENGTH = 500;

@Component({
  selector: 'app-game-detail',
  imports: [GameImageComponent, RawgAttributionComponent, LibraryEntryComponent],
  templateUrl: './game-detail.html',
  styles: `
    :host {
      display: block;
      max-height: 100dvh;
      overflow-y: auto;
    }
    @media (min-width: 640px) {
      :host {
        max-height: 90dvh;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameDetailComponent implements OnInit {
  protected readonly game = inject<GameSummary>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<void>>(DialogRef);
  private readonly catalog = inject(GameCatalogService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly detail = signal<GameDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly retryable = signal(false);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly expanded = signal(false);
  protected readonly summary = computed(() => this.detail()?.summary?.trim() ?? '');
  protected readonly isLongSummary = computed(
    () => this.summary().length > GAME_SUMMARY_PREVIEW_LENGTH,
  );
  protected readonly visibleSummary = computed(() =>
    this.isLongSummary() && !this.expanded()
      ? this.summary().slice(0, GAME_SUMMARY_PREVIEW_LENGTH).trimEnd() + '…'
      : this.summary(),
  );
  protected readonly releaseDate = computed(() => {
    const value = this.detail()?.releaseDate;
    if (!value) return null;
    // The API returns a calendar date. UTC prevents the previous day appearing in Argentina.
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isNaN(date.getTime())
      ? null
      : new Intl.DateTimeFormat('es-AR', { dateStyle: 'long', timeZone: 'UTC' }).format(date);
  });

  ngOnInit(): void {
    this.loadDetail();
  }

  protected retry(): void {
    if (this.retryable()) this.loadDetail();
  }

  private loadDetail(): void {
    if (this.loading() || this.cooldown.remaining() > 0) return;
    this.loading.set(true);
    this.error.set(null);
    this.retryable.set(false);
    this.catalog
      .getDetail(this.game.externalId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 401) return; // Session handling belongs to the interceptor.
          if (this.cooldown.handle(error)) {
            this.error.set('Ya podés reintentar la carga del detalle.');
            this.retryable.set(true);
            return;
          }
          const problem =
            error instanceof HttpErrorResponse ? (error.error as ProblemDetails | null) : null;
          const code = problem?.extensions?.code ?? problem?.code;
          const notFound =
            code === 'game_not_found' ||
            (error instanceof HttpErrorResponse && error.status === 404);
          this.retryable.set(!notFound);
          this.error.set(
            notFound
              ? 'No encontramos este juego'
              : code === 'catalog_unavailable'
                ? 'El catálogo no está disponible en este momento, probá más tarde'
                : 'No pudimos cargar el detalle del juego. Revisá tu conexión e intentá de nuevo.',
          );
        },
      });
  }
}
