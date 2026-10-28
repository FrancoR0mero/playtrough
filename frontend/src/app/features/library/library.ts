import { Dialog, DialogRef } from '@angular/cdk/dialog';
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
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subject,
  catchError,
  exhaustMap,
  expand,
  finalize,
  map,
  merge,
  of,
  reduce,
  switchMap,
  timer,
} from 'rxjs';
import { AuthService } from '../auth/services/auth';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { GAME_CARD_ASPECT_RATIO, GameCardComponent } from '../games/game-card';
import { GameDetailComponent } from '../games/game-detail';
import { GameSummary } from '../games/models/game';
import { ConfirmLibraryRemovalComponent } from './confirm-library-removal';
import { LibraryCardMenuComponent, LibraryMenuAction } from './library-card-menu';
import { localCalendarDate } from './library-date';
import { libraryErrorCode, libraryErrorMessage } from './library-error';
import {
  LIBRARY_STATUSES,
  LibraryChange,
  LibraryEntryDto,
  LibraryQuery,
  LibraryResponse,
  LibrarySort,
  LibraryStatus,
} from './models/library';
import { LibraryService } from './services/library';

const PAGE_SIZE = 20;
const EMPTY_COUNTS: LibraryResponse['counts'] = {
  playing: 0,
  backlog: 0,
  beaten: 0,
  completed: 0,
  total: 0,
};
const SORTS = [
  { value: 'recent', label: 'Cambios recientes' },
  { value: 'added', label: 'Agregados recientemente' },
  { value: 'name', label: 'Nombre' },
  { value: 'release', label: 'Lanzamiento' },
] as const;
interface Filters {
  status: LibraryStatus | '';
  q: string;
  sort: LibrarySort;
}
interface PageRequest {
  page: number;
  rebuild: boolean;
}

@Component({
  selector: 'app-library',
  imports: [RouterLink, ReactiveFormsModule, GameCardComponent, LibraryCardMenuComponent],
  templateUrl: './library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly library = inject(LibraryService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly dialogs = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly queries = new Subject<{ filters: Filters; delay: number }>();
  private readonly pages = new Subject<PageRequest>();
  private readonly known = new Map<number, LibraryEntryDto>();
  private page = 0;
  private dirtyPagination = false;
  private failedPage: PageRequest = { page: 1, rebuild: false };
  private preview: DialogRef<void, GameDetailComponent> | null = null;
  private confirmation: DialogRef<boolean, ConfirmLibraryRemovalComponent> | null = null;
  protected readonly states = LIBRARY_STATUSES;
  protected readonly tabs = [
    { value: '' as const, label: 'Todos', count: 'total' as const },
    ...LIBRARY_STATUSES.map((state) => ({
      value: state.value,
      label: state.tabLabel,
      count: state.value,
    })),
  ];
  protected readonly sorts = SORTS;
  protected readonly filters = signal<Filters>({ status: '', q: '', sort: 'recent' });
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly sort = new FormControl<LibrarySort>('recent', { nonNullable: true });
  protected readonly items = signal<LibraryEntryDto[]>([]);
  protected readonly counts = signal<LibraryResponse['counts']>({ ...EMPTY_COUNTS });
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly notice = signal('');
  protected readonly retryable = signal(false);
  protected readonly invalidQuery = signal(false);
  protected readonly saving = signal<number | null>(null);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly aspectRatio = GAME_CARD_ASPECT_RATIO;
  protected readonly skeletons = Array.from({ length: 8 }, (_, index) => index);

  constructor() {
    this.destroyRef.onDestroy(() => {
      this.preview?.close();
      this.confirmation?.close();
    });
  }

  ngOnInit(): void {
    this.queries
      .pipe(
        switchMap(({ filters, delay }) => {
          this.filters.set(filters);
          this.page = 0;
          this.dirtyPagination = false;
          this.failedPage = { page: 1, rebuild: false };
          this.known.clear();
          this.items.set([]);
          this.hasMore.set(false);
          this.loaded.set(false);
          this.error.set(null);
          this.invalidQuery.set(false);
          this.retryable.set(false);
          this.loading.set(true);
          // After a menu 429, refresh only the GET once the server's cooldown expires.
          // Failed writes are never automatically repeated.
          const wait = Math.max(delay, this.cooldown.remaining() * 1000);
          return (wait ? timer(wait) : of(0)).pipe(
            switchMap(() =>
              merge(of({ page: 1, rebuild: false }), this.pages).pipe(
                exhaustMap((request) => this.fetch(filters, request)),
              ),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ response, replace }) => {
        this.items.set(this.unique([...(replace ? [] : this.items()), ...response.items]));
        this.counts.set(response.counts);
        this.page = response.page;
        this.dirtyPagination = false;
        this.hasMore.set(response.hasMore);
        this.loaded.set(true);
        for (const entry of response.items) this.known.set(entry.gameId, entry);
      });

    let initialized = false;
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const status = this.states.find((state) => state.value === params.get('status'))?.value ?? '';
      const sort = SORTS.find((option) => option.value === params.get('sort'))?.value ?? 'recent';
      const next: Filters = { status, sort, q: (params.get('q') ?? '').trim() };
      if (initialized && this.sameFilters(next, this.filters())) return;
      initialized = true;
      this.search.setValue(next.q, { emitEvent: false });
      this.sort.setValue(next.sort, { emitEvent: false });
      this.queries.next({ filters: next, delay: 0 });
    });
    this.search.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((q) => this.setFilters({ ...this.filters(), q: q.trim() }, 400));
    this.sort.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((sort) => this.setFilters({ ...this.filters(), sort }, 0));
    this.library.changes$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((change) => this.applyChange(change));
    this.library.invalidated$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((error) => {
      if (error instanceof HttpErrorResponse && error.status === 401) return;
      this.actionError.set(
        this.cooldown.handle(error)
          ? 'No se guardó el cambio. Actualizaremos la lista al terminar la espera.'
          : libraryErrorMessage(error),
      );
      this.reload();
      if (!this.preview) this.focusTab();
    });
  }

  private fetch(filters: Filters, request: PageRequest) {
    this.loading.set(true);
    this.error.set(null);
    this.retryable.set(false);
    this.invalidQuery.set(false);
    this.failedPage = request;
    const query: LibraryQuery = {
      ...filters,
      status: filters.status || undefined,
      pageSize: PAGE_SIZE,
    };
    const page = (number: number) => this.library.list({ ...query, page: number });
    // Mutations can shift offset-based pages. Re-read the prefix before loading more,
    // so a deletion or reordering cannot silently skip the next unseen game.
    const result = request.rebuild
      ? page(1).pipe(
          expand((response) =>
            response.hasMore && response.page < request.page ? page(response.page + 1) : EMPTY,
          ),
          reduce(
            (previous: LibraryResponse | null, response) => ({
              ...response,
              items: this.unique([...(previous?.items ?? []), ...response.items]),
            }),
            null,
          ),
          map((response) => response!),
        )
      : page(request.page);
    return result.pipe(
      map((response) => ({ response, replace: request.rebuild || request.page === 1 })),
      catchError((error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) return EMPTY;
        this.invalidQuery.set(libraryErrorCode(error) === 'invalid_query');
        this.retryable.set(!this.invalidQuery());
        this.error.set(
          this.cooldown.handle(error)
            ? 'Ya podés reintentar la carga de tu biblioteca.'
            : libraryErrorMessage(error, false),
        );
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
    );
  }

  private setFilters(filters: Filters, delay: number): void {
    if (this.sameFilters(filters, this.filters())) return;
    this.actionError.set(null);
    this.queries.next({ filters, delay });
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        status: filters.status || null,
        q: filters.q || null,
        sort: filters.sort === 'recent' ? null : filters.sort,
      },
      queryParamsHandling: 'merge',
      replaceUrl: delay > 0,
    });
  }

  protected chooseTab(status: LibraryStatus | ''): void {
    this.setFilters({ ...this.filters(), status }, 0);
  }

  protected tabKey(event: KeyboardEvent, index: number): void {
    if (!['ArrowRight', 'ArrowLeft', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    const count = this.tabs.length;
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? count - 1
          : (index + (event.key === 'ArrowRight' ? 1 : -1) + count) % count;
    this.chooseTab(this.tabs[next].value);
    this.element.nativeElement.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }

  protected loadMore(): void {
    if (
      this.loading() ||
      this.error() ||
      !this.hasMore() ||
      this.cooldown.remaining() ||
      this.saving() !== null
    )
      return;
    this.pages.next({ page: this.page + 1, rebuild: this.dirtyPagination });
  }

  protected retry(): void {
    if (!this.loading() && this.retryable() && !this.cooldown.remaining())
      this.pages.next(this.failedPage);
  }

  private reload(): void {
    this.queries.next({ filters: this.filters(), delay: 0 });
  }

  protected state(status: LibraryStatus) {
    return this.states.find((state) => state.value === status)!;
  }

  protected openGame(entry: LibraryEntryDto, origin: HTMLButtonElement): void {
    if (this.preview || this.confirmation || this.saving() !== null) return;
    const game: GameSummary = { ...entry, platforms: [] };
    this.preview = this.dialogs.open<void, GameSummary, GameDetailComponent>(GameDetailComponent, {
      data: game,
      role: 'dialog',
      ariaModal: true,
      ariaLabelledBy: 'game-detail-title',
      autoFocus: '[data-action="close"]',
      restoreFocus: origin,
      width: '100vw',
      maxWidth: '48rem',
    });
    this.preview.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.preview = null;
      if (!origin.isConnected)
        afterNextRender(
          () => {
            const card = this.element.nativeElement.querySelector<HTMLButtonElement>(
              `[data-library-game-id="${entry.gameId}"] [data-game-card]`,
            );
            (
              card ??
              this.element.nativeElement.querySelector<HTMLButtonElement>(
                '[role="tab"][aria-selected="true"]',
              )
            )?.focus();
          },
          { injector: this.injector },
        );
    });
  }

  protected menuAction(entry: LibraryEntryDto, action: LibraryMenuAction): void {
    if (
      this.saving() !== null ||
      this.cooldown.remaining() ||
      this.loading() ||
      this.preview ||
      this.confirmation
    )
      return;
    if (action.value !== 'remove') {
      if (entry.status !== action.value)
        this.mutate(
          entry,
          this.library.changeStatus(entry.gameId, action.value, localCalendarDate()),
          'Estado actualizado',
          action.origin,
        );
      return;
    }
    this.confirmation = this.dialogs.open<boolean, string, ConfirmLibraryRemovalComponent>(
      ConfirmLibraryRemovalComponent,
      {
        data: entry.name,
        role: 'dialog',
        ariaModal: true,
        ariaLabelledBy: 'library-removal-title',
        ariaDescribedBy: 'library-removal-description',
        autoFocus: '[data-action="cancel"]',
        restoreFocus: action.origin,
        width: 'calc(100vw - 2rem)',
        maxWidth: '32rem',
      },
    );
    this.confirmation.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((confirmed) => {
      this.confirmation = null;
      if (confirmed)
        this.mutate(
          entry,
          this.library.remove(entry.gameId),
          'Quitado de tu biblioteca',
          action.origin,
        );
      else if (!action.origin.isConnected) this.focusTab();
    });
  }

  private mutate(
    entry: LibraryEntryDto,
    operation: Observable<LibraryEntryDto | void>,
    message: string,
    origin: HTMLButtonElement,
  ): void {
    if (this.saving() !== null) return;
    this.saving.set(entry.gameId);
    this.notice.set('');
    this.actionError.set(null);
    operation
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(null)),
      )
      .subscribe({
        next: () => {
          this.notice.set(message);
          afterNextRender(
            () => {
              const target =
                origin.isConnected && !this.loading()
                  ? origin
                  : this.element.nativeElement.querySelector<HTMLButtonElement>(
                      '[role="tab"][aria-selected="true"]',
                    );
              target?.focus();
            },
            { injector: this.injector },
          );
        },
        // The service invalidation stream handles errors from both menus and the modal.
        error: () => undefined,
      });
  }

  private applyChange(change: LibraryChange): void {
    if (this.loading() || !this.loaded()) {
      this.reload();
      return;
    }
    const previous = this.known.get(change.gameId);
    if ((change.type !== 'added' && !previous) || (change.type === 'added' && previous)) {
      this.reload();
      return;
    }
    const counts = { ...this.counts() };
    if (change.type === 'removed') {
      counts[previous!.status]--;
      counts.total--;
    } else {
      if (previous) counts[previous.status]--;
      else counts.total++;
      counts[change.entry.status]++;
    }
    if (
      Object.values(counts).some((count) => count < 0) ||
      this.states.reduce((sum, state) => sum + counts[state.value], 0) !== counts.total
    ) {
      this.reload();
      return;
    }
    let orderChanged = false;
    this.counts.set(counts);
    if (change.type === 'removed') {
      this.known.delete(change.gameId);
      this.items.update((items) => items.filter((item) => item.gameId !== change.gameId));
    } else {
      this.known.set(change.gameId, change.entry);
      const matches = !this.filters().status || this.filters().status === change.entry.status;
      const present = this.items().some((item) => item.gameId === change.gameId);
      orderChanged = matches && (!present || this.filters().sort === 'recent');
      if (this.filters().q && !present) {
        this.reload();
        return;
      }
      this.items.update((items) =>
        matches
          ? present
            ? items.map((item) => (item.gameId === change.gameId ? change.entry : item))
            : [change.entry, ...items]
          : items.filter((item) => item.gameId !== change.gameId),
      );
    }
    this.dirtyPagination = true;
    if (counts[this.filters().status || 'total'] === 0) this.hasMore.set(false);
    if (!this.items().length && this.hasMore()) this.reload();
    else if (orderChanged) {
      // Keep the optimistic badge/counts visible, then use the server's ordering
      // (including tie breakers) instead of guessing it from calendar dates.
      this.pages.next({ page: Math.max(1, this.page), rebuild: true });
    }
  }

  private unique(items: LibraryEntryDto[]): LibraryEntryDto[] {
    return [...new Map(items.map((entry) => [entry.gameId, entry])).values()];
  }
  private sameFilters(a: Filters, b: Filters): boolean {
    return a.status === b.status && a.q === b.q && a.sort === b.sort;
  }
  private focusTab(): void {
    afterNextRender(
      () =>
        this.element.nativeElement
          .querySelector<HTMLButtonElement>('[role="tab"][aria-selected="true"]')
          ?.focus(),
      { injector: this.injector },
    );
  }
}
