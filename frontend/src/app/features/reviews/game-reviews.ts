import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subscription, combineLatest, finalize } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { GameImageComponent } from '../games/game-image';
import { GameDetail } from '../games/models/game';
import { GameCatalogService } from '../games/services/game-catalog';
import { MyReviewDto, PublicReviewDto, RatingSummary, ReviewSort } from './models/review';
import { RatingSummaryComponent } from './rating-summary';
import { ReviewContentComponent } from './review-content';
import { reviewErrorCode } from './review-error';
import { ReviewService } from './services/review';

type Section = 'detail' | 'mine' | 'list';
const SORTS = [
  { value: 'recent', label: 'Más recientes' },
  { value: 'top', label: 'Mejor puntuadas' },
  { value: 'low', label: 'Peor puntuadas' },
] as const;

@Component({
  selector: 'app-game-reviews',
  imports: [
    RouterLink,
    ReactiveFormsModule,
    GameImageComponent,
    RatingSummaryComponent,
    ReviewContentComponent,
  ],
  templateUrl: './game-reviews.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameReviewsComponent implements OnInit {
  private readonly catalog = inject(GameCatalogService);
  private readonly reviews = inject(ReviewService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private readonly title = viewChild<ElementRef<HTMLElement>>('title');
  // Capture entry history once. Replacing ?sort must not turn a direct entry into a back target.
  private readonly hasPrevious = !!this.router.currentNavigation()?.previousNavigation;
  private detailRequest?: Subscription;
  private mineRequest?: Subscription;
  private listRequest?: Subscription;
  private externalId = '';
  private page = 0;
  private failedPage = 1;
  protected readonly detail = signal<GameDetail | null>(null);
  protected readonly mine = signal<MyReviewDto | null>(null);
  protected readonly items = signal<PublicReviewDto[]>([]);
  protected readonly summary = signal<RatingSummary>({ average: null, count: 0 });
  protected readonly sorts = SORTS;
  protected readonly sort = new FormControl<ReviewSort>('recent', { nonNullable: true });
  private selectedSort: ReviewSort = 'recent';
  protected readonly loading = { detail: signal(false), mine: signal(false), list: signal(false) };
  protected readonly errors = {
    detail: signal<string | null>(null),
    mine: signal<string | null>(null),
    list: signal<string | null>(null),
  };
  protected readonly cooldowns = {
    detail: createRateLimitCooldown(),
    mine: createRateLimitCooldown(),
    list: createRateLimitCooldown(),
  };
  protected readonly notFound = signal(false);
  protected readonly listLoaded = signal(false);
  protected readonly hasMore = signal(false);

  ngOnInit(): void {
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(([params, query]) => {
        const externalId = params.get('externalId') ?? '';
        const sort = SORTS.find((option) => option.value === query.get('sort'))?.value ?? 'recent';
        const changedSort = this.selectedSort !== sort;
        this.selectedSort = sort;
        this.sort.setValue(sort, { emitEvent: false });
        if (this.externalId !== externalId) {
          this.externalId = externalId;
          this.loadDetail();
        } else if (changedSort && this.detail()) this.resetList();
      });
    this.sort.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((sort) => {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: { sort },
        queryParamsHandling: 'merge',
        replaceUrl: true,
      });
    });
    this.reviews.changes$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((change) => {
      if (change.gameId !== this.detail()?.id) return;
      this.mineRequest?.unsubscribe();
      this.mine.set(change.type === 'deleted' ? null : change.review);
      this.errors.mine.set(null);
      this.summary.set(change.ratingSummary);
      this.resetList();
    });
    this.focusTitle();
  }

  protected back(): void {
    if (this.hasPrevious) this.location.back();
    else void this.router.navigate(['/games']);
  }

  protected loadDetail(): void {
    this.detailRequest?.unsubscribe();
    this.mineRequest?.unsubscribe();
    this.listRequest?.unsubscribe();
    this.detail.set(null);
    this.mine.set(null);
    this.items.set([]);
    this.listLoaded.set(false);
    this.hasMore.set(false);
    this.notFound.set(false);
    for (const section of ['detail', 'mine', 'list'] as const) this.errors[section].set(null);
    if (
      !/^\d+$/.test(this.externalId) ||
      Number(this.externalId) <= 0 ||
      !Number.isSafeInteger(Number(this.externalId))
    ) {
      this.notFound.set(true);
      this.errors.detail.set('No encontramos este juego');
      return;
    }
    if (this.cooldowns.detail.remaining()) {
      this.errors.detail.set('Ya podés reintentar la carga del juego.');
      return;
    }
    this.loading.detail.set(true);
    this.detailRequest = this.catalog
      .getDetail(Number(this.externalId))
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.detail.set(false)),
      )
      .subscribe({
        next: (detail) => {
          this.detail.set(detail);
          this.summary.set(detail.ratingSummary);
          this.focusTitle();
          this.loadMine();
          this.resetList();
        },
        error: (error: unknown) => this.handleError('detail', error),
      });
  }

  protected loadMine(): void {
    const game = this.detail();
    if (!game || this.loading.mine() || this.notFound()) return;
    if (this.cooldowns.mine.remaining()) {
      this.errors.mine.set('Ya podés reintentar la carga de tu reseña.');
      return;
    }
    this.errors.mine.set(null);
    this.loading.mine.set(true);
    this.mineRequest = this.reviews
      .getMine(game.id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.mine.set(false)),
      )
      .subscribe({
        next: (review) => this.mine.set(review),
        error: (error: unknown) => {
          if (reviewErrorCode(error) === 'review_not_found') this.mine.set(null);
          else this.handleError('mine', error);
        },
      });
  }

  protected loadMore(): void {
    if (this.hasMore()) this.loadPage(this.page + 1);
  }
  protected retryList(): void {
    this.loadPage(this.failedPage);
  }

  private resetList(): void {
    this.listRequest?.unsubscribe();
    this.items.set([]);
    this.page = 0;
    this.failedPage = 1;
    this.hasMore.set(false);
    this.listLoaded.set(false);
    this.errors.list.set(null);
    if (this.cooldowns.list.remaining()) {
      this.errors.list.set('Ya podés reintentar la carga de las reseñas.');
      return;
    }
    this.loadPage(1);
  }

  private loadPage(page: number): void {
    const game = this.detail();
    if (!game || this.loading.list() || this.cooldowns.list.remaining() || this.notFound()) return;
    this.failedPage = page;
    this.loading.list.set(true);
    this.errors.list.set(null);
    this.listRequest = this.reviews
      .list(game.id, this.selectedSort, page, 20)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.list.set(false)),
      )
      .subscribe({
        next: (response) => {
          const unique = new Map(this.items().map((item) => [item.id, item]));
          for (const item of response.items) {
            if (!item.isMine) unique.set(item.id, item);
          }
          this.items.set([...unique.values()]);
          this.summary.set(response.ratingSummary);
          this.page = response.page;
          this.hasMore.set(response.hasMore);
          this.listLoaded.set(true);
        },
        error: (error: unknown) => this.handleError('list', error),
      });
  }

  private handleError(section: Section, error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 401) return;
    if (this.cooldowns[section].handle(error)) {
      this.errors[section].set('Ya podés reintentar la carga.');
      return;
    }
    const code = reviewErrorCode(error);
    if (code === 'game_not_found') {
      this.notFound.set(true);
      this.errors[section].set('No encontramos este juego');
    } else
      this.errors[section].set(
        code === 'catalog_unavailable'
          ? 'El catálogo no está disponible en este momento, probá más tarde'
          : 'No pudimos cargar las reseñas. Revisá tu conexión e intentá de nuevo.',
      );
  }

  private focusTitle(): void {
    afterNextRender(() => this.title()?.nativeElement.focus(), { injector: this.injector });
  }
}
