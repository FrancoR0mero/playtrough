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
import { ActivatedRoute, Router } from '@angular/router';
import {
  EMPTY,
  Subject,
  catchError,
  exhaustMap,
  finalize,
  merge,
  of,
  switchMap,
  timer,
} from 'rxjs';
import { PeopleSearchState } from '../../shared/components/people-search/people-search-state';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { FriendRowComponent } from '../friends/friend-row';
import { FriendshipButtonComponent } from '../friends/friendship-button';
import { RelationshipDto } from '../friends/models/friend';
import { UserSearchDto } from '../profile/models/user';
import { profileError } from '../profile/profile-error';
import { UsersService } from '../profile/services/users';

@Component({
  selector: 'app-people',
  imports: [FriendRowComponent, FriendshipButtonComponent],
  templateUrl: './people.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PeopleComponent implements OnInit {
  private readonly users = inject(UsersService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly queries = new Subject<{ q: string; delay: number }>();
  private readonly pages = new Subject<number>();
  private query = '';
  private page = 0;
  private failedPage = 1;
  private readonly searchState = inject(PeopleSearchState);
  protected readonly search = this.searchState.control;
  protected readonly items = signal<UserSearchDto[]>([]);
  protected readonly loading = signal(false);
  protected readonly searched = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly invalidQuery = this.searchState.invalid;
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
  }

  ngOnInit(): void {
    this.queries
      .pipe(
        switchMap(({ q, delay }) => {
          this.query = q;
          this.page = 0;
          this.failedPage = 1;
          this.items.set([]);
          this.hasMore.set(false);
          this.error.set(null);
          this.invalidQuery.set(false);
          this.searched.set(false);
          this.loading.set(false);
          if (Array.from(q).length < 2) return EMPTY;
          if (this.cooldown.remaining()) {
            this.error.set('Podés reintentar cuando termine la espera.');
            return this.pages.pipe(exhaustMap((page) => this.fetch(q, page)));
          }
          this.loading.set(true);
          return timer(delay).pipe(
            switchMap(() =>
              merge(of(1), this.pages).pipe(exhaustMap((page) => this.fetch(q, page))),
            ),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => {
        const unique = new Map(this.items().map((user) => [user.username.toLowerCase(), user]));
        for (const user of result.items)
          if (!unique.has(user.username.toLowerCase()))
            unique.set(user.username.toLowerCase(), user);
        this.items.set([...unique.values()]);
        this.page = result.page;
        this.hasMore.set(result.hasMore);
        this.searched.set(true);
      });
    let initialized = false;
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const q = (params.get('q') ?? '').trim();
      if (initialized && q === this.query) return;
      initialized = true;
      this.searchState.restore(q);
      this.queries.next({ q, delay: 0 });
    });
    this.search.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => {
      const q = value.trim();
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

  private fetch(q: string, page: number) {
    this.loading.set(true);
    this.error.set(null);
    this.invalidQuery.set(false);
    return this.users.search(q, page).pipe(
      catchError((error) => {
        this.failedPage = page;
        if (this.cooldown.handle(error)) {
          this.error.set('Podés reintentar cuando termine la espera.');
          return EMPTY;
        }
        const invalid = profileError(error).code === 'invalid_query';
        this.invalidQuery.set(invalid);
        this.error.set(
          invalid
            ? 'La búsqueda no es válida. Escribí al menos 2 caracteres y revisá el texto.'
            : 'No pudimos buscar personas. Revisá tu conexión e intentá de nuevo.',
        );
        return EMPTY;
      }),
      finalize(() => this.loading.set(false)),
    );
  }
  protected more(): void {
    if (!this.loading() && this.hasMore() && !this.error() && !this.cooldown.remaining())
      this.pages.next(this.page + 1);
  }
  protected retry(): void {
    if (!this.loading() && this.error() && !this.invalidQuery() && !this.cooldown.remaining())
      this.pages.next(this.failedPage);
  }
  protected updateRelationship(username: string, relationship: RelationshipDto): void {
    this.items.update((items) =>
      items.map((user) => (user.username === username ? { ...user, relationship } : user)),
    );
  }
}
