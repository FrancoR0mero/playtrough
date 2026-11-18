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
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationStart, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { AuthService } from '../auth/services/auth';
import { EntryCommentsComponent } from './entry-comments';
import { EntryNavigation, EntryOrigin } from './entry-navigation';
import { FeedEntryDto } from './models/timeline';
import { TimelineService } from './services/timeline';
import { TimelineEntryCardComponent } from './timeline-entry-card';

@Component({
  selector: 'app-entry-detail',
  imports: [RouterLink, TimelineEntryCardComponent, EntryCommentsComponent],
  templateUrl: './entry-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntryDetailComponent implements OnInit {
  private readonly service = inject(TimelineService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  private readonly navigation = inject(EntryNavigation);
  private readonly auth = inject(AuthService);
  private readonly title = viewChild<ElementRef<HTMLElement>>('title');
  private readonly comments = viewChild(EntryCommentsComponent);
  private readonly origin =
    (this.router.currentNavigation()?.extras.state?.['entryOrigin'] as EntryOrigin | undefined) ??
    (this.location.getState() as { entryOrigin?: EntryOrigin } | null)?.entryOrigin;
  private readonly hasPrevious = !!(
    this.origin || this.router.currentNavigation()?.previousNavigation
  );
  private request?: Subscription;
  private id = 0;
  private focusOnLoad = false;
  protected readonly entry = signal<FeedEntryDto | null>(null);
  protected readonly count = signal(0);
  protected readonly loading = signal(false);
  protected readonly unavailable = signal(false);
  protected readonly failed = signal(false);
  protected readonly cooldown = createRateLimitCooldown();
  protected readonly own = computed(
    () =>
      this.auth.currentUser()?.username.toLowerCase() ===
      this.entry()?.author.username.toLowerCase(),
  );

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      const raw = params.get('id') ?? '';
      this.id = /^\d+$/.test(raw) && Number.isSafeInteger(Number(raw)) ? Number(raw) : 0;
      this.focusOnLoad = this.route.snapshot.queryParamMap.get('comment') === '1';
      this.load();
    });
    this.router.events.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (
        event instanceof NavigationStart &&
        event.navigationTrigger === 'popstate' &&
        event.url === this.origin?.url
      )
        this.navigation.prepareReturn(this.origin);
    });
  }

  ngOnInit(): void {
    this.focusTitle();
  }

  protected back(): void {
    if (this.hasPrevious) this.location.back();
    else void this.router.navigate(['/feed']);
  }

  protected load(): void {
    if (this.cooldown.remaining()) return;
    this.request?.unsubscribe();
    this.entry.set(null);
    this.unavailable.set(false);
    this.failed.set(false);
    if (!this.id) {
      this.markUnavailable();
      return;
    }
    this.loading.set(true);
    this.request = this.service
      .getEntry(this.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (entry) => {
          this.count.set(entry.commentCount);
          this.entry.set(entry);
          this.loading.set(false);
          if (this.focusOnLoad) {
            this.focusOnLoad = false;
            afterNextRender(() => this.focusComposer(), { injector: this.injector });
          }
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (error instanceof HttpErrorResponse && error.status === 404) this.markUnavailable();
          else {
            this.cooldown.handle(error);
            this.failed.set(true);
          }
        },
      });
  }

  protected markUnavailable(): void {
    this.entry.set(null);
    this.loading.set(false);
    this.unavailable.set(true);
    this.focusTitle();
  }

  protected focusComposer(): void {
    this.comments()?.focusComposer();
  }

  private focusTitle(): void {
    afterNextRender(() => this.title()?.nativeElement.focus(), { injector: this.injector });
  }
}
