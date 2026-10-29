import { Dialog, DialogRef } from '@angular/cdk/dialog';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  EMPTY,
  Subject,
  catchError,
  exhaustMap,
  finalize,
  map,
  merge,
  of,
  switchMap,
  timer,
} from 'rxjs';
import { ProblemDetails } from '../auth/models/register';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { GAME_CARD_ASPECT_RATIO, GameCardComponent } from './game-card';
import { GameDetailComponent } from './game-detail';
import { GameSummary } from './models/game';
import { GameCatalogService } from './services/game-catalog';

export { GAME_CARD_ASPECT_RATIO } from './game-card';
const PAGE_SIZE = 20;

@Component({
  selector: 'app-games',
  imports: [ReactiveFormsModule, GameCardComponent],
  templateUrl: './games.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GamesComponent implements OnInit {
  private readonly catalog = inject(GameCatalogService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialogs = inject(Dialog);
  private readonly queries = new Subject<{ q: string; delay: number }>();
  private readonly pages = new Subject<number>();
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');
  private preview: DialogRef<void, GameDetailComponent> | null = null;
  private query = '';
  private page = 0;
  private failedPage = 1;

  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly aspectRatio = GAME_CARD_ASPECT_RATIO;
  protected readonly skeletons = Array.from({ length: 8 }, (_, index) => index);
  protected readonly items = signal<GameSummary[]>([]);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly isLimited = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly retryable = signal(false);
  protected readonly invalidQuery = signal(false);
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    this.destroyRef.onDestroy(() => this.preview?.close());
    afterNextRender(() => {
      if (this.route.snapshot.queryParamMap.get('focus') === 'search') {
        this.searchInput()?.nativeElement.focus();
      }
    });
  }

  ngOnInit(): void {
    this.queries
      .pipe(
        // Cancel immediately on input, including during the next debounce and when clearing the field.
        switchMap(({ q, delay }) => {
          this.query = q;
          this.page = 0;
          this.failedPage = 1;
          this.items.set([]);
          this.hasMore.set(false);
          this.isLimited.set(false);
          this.error.set(null);
          this.retryable.set(false);
          this.invalidQuery.set(false);
          this.searched.set(false);
          this.loading.set(false);
          if (q.length < 2) return EMPTY;
          if (this.cooldown.remaining() > 0) {
            this.error.set('Ya podés reintentar la búsqueda.');
            this.retryable.set(true);
            return this.pages.pipe(exhaustMap((page) => this.fetchPage(q, page)));
          }
          this.loading.set(true);
          return timer(delay).pipe(
            switchMap(() =>
              merge(of(1), this.pages).pipe(exhaustMap((page) => this.fetchPage(q, page))),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((response) => {
        const unique = new Map(this.items().map((game) => [game.externalId, game]));
        for (const game of response.items)
          if (!unique.has(game.externalId)) unique.set(game.externalId, game);
        this.items.set([...unique.values()]);
        this.page = response.page;
        this.hasMore.set(response.hasMore);
        this.isLimited.set(response.isLimited ?? false);
        this.searched.set(true);
      });

    let initialized = false;
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const q = (params.get('q') ?? '').trim();
      if (initialized && q === this.query) return;
      initialized = true;
      this.search.setValue(q, { emitEvent: false });
      this.queries.next({ q, delay: 0 });
    });
    this.search.valueChanges
      .pipe(
        map((value) => value.trim()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((q) => {
        if (q === this.query) return;
        this.queries.next({ q, delay: 400 });
        void this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { q: q || null },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      });
  }

  private fetchPage(q: string, page: number) {
    this.loading.set(true);
    this.error.set(null);
    this.retryable.set(false);
    this.invalidQuery.set(false);
    return this.catalog.search(q, page, PAGE_SIZE).pipe(
      catchError((error: unknown) => {
        this.failedPage = page;
        if (error instanceof HttpErrorResponse && error.status === 401) return EMPTY;
        if (this.cooldown.handle(error)) {
          this.error.set('Ya podés reintentar la búsqueda.');
          this.retryable.set(true);
          return EMPTY;
        }
        const problem =
          error instanceof HttpErrorResponse ? (error.error as ProblemDetails | null) : null;
        const code = problem?.extensions?.code ?? problem?.code;
        this.invalidQuery.set(code === 'invalid_query');
        this.retryable.set(code !== 'invalid_query');
        this.error.set(
          code === 'invalid_query'
            ? 'La búsqueda no es válida. Ingresá al menos 2 caracteres y revisá el texto.'
            : code === 'catalog_unavailable'
              ? 'El catálogo no está disponible en este momento, probá más tarde'
              : 'No pudimos cargar los juegos. Revisá tu conexión e intentá de nuevo.',
        );
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
    );
  }

  protected loadMore(): void {
    if (this.loading() || !this.hasMore() || this.error() || this.cooldown.remaining()) return;
    this.pages.next(this.page + 1);
  }

  protected retry(): void {
    if (this.loading() || !this.retryable() || this.cooldown.remaining() || this.query.length < 2)
      return;
    this.pages.next(this.failedPage);
  }

  protected openGame(game: GameSummary, origin: HTMLButtonElement): void {
    if (this.preview) return;
    this.preview = this.dialogs.open<void, GameSummary, GameDetailComponent>(GameDetailComponent, {
      data: game,
      role: 'dialog',
      ariaModal: true,
      ariaLabelledBy: 'game-detail-title',
      autoFocus: '[data-action="close"]',
      restoreFocus: origin,
      width: '100vw',
      maxWidth: '64rem',
    });
    this.preview.closed
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => (this.preview = null));
  }
}
