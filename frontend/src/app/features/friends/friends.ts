import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, Subscription, combineLatest, debounceTime, tap } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { profileError } from '../profile/profile-error';
import { FriendRowComponent } from './friend-row';
import { FriendshipButtonComponent } from './friendship-button';
import {
  FRIENDSHIP_MESSAGES,
  FriendDto,
  FriendRequestDto,
  FriendsPage,
  RelationshipDto,
} from './models/friend';
import { FriendsService } from './services/friends';
import { FriendsState } from './services/friends-state';

type Tab = 'friends' | 'received' | 'sent';
type Row = FriendDto | FriendRequestDto;

@Component({
  selector: 'app-friends',
  imports: [RouterLink, ReactiveFormsModule, FriendRowComponent, FriendshipButtonComponent],
  templateUrl: './friends.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FriendsComponent {
  private readonly service = inject(FriendsService);
  protected readonly state = inject(FriendsState);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private request?: Subscription;
  private page = 0;
  private failedPage = 1;
  private query = '';
  protected readonly username = signal<string | null>(null);
  protected readonly tab = signal<Tab>('friends');
  protected readonly tabs = [
    { id: 'friends' as const, label: 'Amigos', count: 'friendsCount' as const },
    { id: 'received' as const, label: 'Recibidas', count: 'pendingReceivedCount' as const },
    { id: 'sent' as const, label: 'Enviadas', count: 'pendingSentCount' as const },
  ];
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly items = signal<Row[]>([]);
  protected readonly loading = signal(false);
  protected readonly searching = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly restricted = signal(false);
  protected readonly status = signal<string | null>(null);
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    afterNextRender(() => this.heading()?.nativeElement.focus());
    this.state.refresh();
    combineLatest([this.route.paramMap, this.route.queryParamMap])
      .pipe(takeUntilDestroyed())
      .subscribe(([params, query]) => {
        this.username.set(params.get('username'));
        const tab = query.get('tab');
        this.tab.set(!this.username() && (tab === 'received' || tab === 'sent') ? tab : 'friends');
        this.query = query.get('q') ?? '';
        this.search.setValue(this.query, { emitEvent: false });
        this.searching.set(false);
        this.status.set(null);
        this.reload();
      });
    this.search.valueChanges
      .pipe(
        tap(() => {
          this.request?.unsubscribe();
          this.loading.set(false);
          this.searching.set(true);
        }),
        debounceTime(400),
        takeUntilDestroyed(),
      )
      .subscribe((q) => {
        if (this.tab() !== 'friends' || this.username()) {
          this.searching.set(false);
          return;
        }
        if (q.trim() === this.query) {
          this.searching.set(false);
          this.reload();
          return;
        }
        void this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { q: q.trim() || null },
          queryParamsHandling: 'merge',
          replaceUrl: true,
        });
      });
    this.service.changes$.pipe(takeUntilDestroyed()).subscribe((change) => {
      this.status.set(FRIENDSHIP_MESSAGES[change.action]);
      this.reload(true);
    });
    this.destroyRef.onDestroy(() => this.request?.unsubscribe());
  }

  protected selectTab(tab: Tab): void {
    if (this.tab() === tab) return;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
    });
  }

  protected keydown(event: KeyboardEvent, index: number): void {
    let next: number;
    if (event.key === 'ArrowRight') next = (index + 1) % 3;
    else if (event.key === 'ArrowLeft') next = (index + 2) % 3;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = 2;
    else return;
    event.preventDefault();
    const button = (
      event.currentTarget as HTMLElement
    ).parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next];
    button?.focus();
    this.selectTab(this.tabs[next].id);
  }

  protected since(row: Row): string | null {
    return 'since' in row ? row.since : null;
  }
  protected relationship(row: Row): RelationshipDto {
    return 'since' in row
      ? { status: 'friends' }
      : {
          status: this.tab() === 'received' ? 'request_received' : 'request_sent',
          requestId: row.id,
        };
  }

  protected reconciled(row: Row, relationship: RelationshipDto): void {
    if (relationship.status !== this.relationship(row).status) {
      this.items.update((rows) => rows.filter((item) => item.user.username !== row.user.username));
      this.panel()?.nativeElement.focus();
    }
  }

  private reload(keepItems = false): void {
    this.request?.unsubscribe();
    this.loading.set(false);
    this.page = 0;
    if (!keepItems) this.items.set([]);
    this.hasMore.set(false);
    this.error.set(null);
    this.restricted.set(false);
    this.fetch(1);
  }
  protected more(): void {
    if (this.hasMore()) this.fetch(this.page + 1);
  }
  protected retry(): void {
    this.fetch(this.failedPage);
  }

  private fetch(page: number): void {
    if (this.loading() || this.searching()) return;
    this.failedPage = page;
    if (this.cooldown.remaining()) {
      this.error.set('Podés reintentar cuando termine la espera.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    const username = this.username();
    let request: Observable<FriendsPage<Row>>;
    if (username) request = this.service.forUser(username, page);
    else if (this.tab() === 'received') request = this.service.received(page);
    else if (this.tab() === 'sent') request = this.service.sent(page);
    else request = this.service.list(this.query, page);
    this.request = request.subscribe({
      next: (result) => {
        const rows = page === 1 ? [] : this.items();
        const unique = new Map(rows.map((row) => [row.user.username.toLowerCase(), row]));
        result.items.forEach((row) => unique.set(row.user.username.toLowerCase(), row));
        this.items.set([...unique.values()]);
        this.page = result.page;
        this.hasMore.set(result.hasMore);
        this.loading.set(false);
      },
      error: (error) => {
        this.loading.set(false);
        if (this.cooldown.handle(error)) {
          this.error.set('Podés reintentar cuando termine la espera.');
          return;
        }
        const code = profileError(error).code;
        if (code === 'profile_restricted') {
          this.items.set([]);
          this.restricted.set(true);
        }
        this.error.set(
          code === 'profile_restricted'
            ? 'La lista de amigos de este perfil es privada.'
            : code === 'user_not_found'
              ? 'No encontramos a este jugador.'
              : 'No pudimos cargar la lista. Revisá tu conexión y reintentá.',
        );
      },
    });
  }
}
