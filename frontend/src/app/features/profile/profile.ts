import { Dialog, DialogRef } from '@angular/cdk/dialog';
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Observable, Subject, Subscription, auditTime, finalize } from 'rxjs';
import { FriendshipButtonComponent } from '../friends/friendship-button';
import { FriendsPreviewComponent } from '../friends/friends-preview';
import { FriendsService } from '../friends/services/friends';
import { RelationshipDto } from '../friends/models/friend';
import { AuthService } from '../auth/services/auth';
import { createRateLimitCooldown } from '../auth/rate-limit-cooldown';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { MyProfileDto, ProfileDto } from './models/user';
import { UsersService } from './services/users';
import { profileError } from './profile-error';
import { ProfileEditComponent, ProfileEditResult } from './profile-edit';

@Component({
  selector: 'app-profile',
  imports: [RouterLink, UserAvatarComponent, FriendshipButtonComponent, FriendsPreviewComponent],
  templateUrl: './profile.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileComponent {
  protected readonly auth = inject(AuthService);
  protected readonly loggingOut = signal(false);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly users = inject(UsersService);
  private readonly dialogs = inject(Dialog);
  private readonly destroyRef = inject(DestroyRef);
  private readonly friends = inject(FriendsService);
  private socialRequest?: Subscription;
  private readonly socialRefresh = new Subject<void>();
  private readonly title = viewChild<ElementRef<HTMLHeadingElement>>('title');
  private request?: Subscription;
  private editor?: DialogRef<ProfileEditResult, ProfileEditComponent>;
  protected readonly own = signal(true);
  protected readonly profile = signal<MyProfileDto | ProfileDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notFound = signal(false);
  protected readonly status = signal<string | null>(null);
  protected readonly warning = signal<string | null>(null);
  protected readonly cooldown = createRateLimitCooldown();

  constructor() {
    this.socialRefresh.pipe(auditTime(0), takeUntilDestroyed()).subscribe(() => this.loadSocial());
    this.friends.changes$.pipe(takeUntilDestroyed()).subscribe(() => this.refreshSocial());
    afterNextRender(() => this.title()?.nativeElement.focus());
    this.route.paramMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.request?.unsubscribe();
      this.socialRequest?.unsubscribe();
      this.editor?.close();
      this.status.set(null);
      this.warning.set(null);
      const username = params.get('username');
      if (username && username.toLowerCase() === this.auth.currentUser()?.username.toLowerCase()) {
        void this.router.navigate(['/profile'], { replaceUrl: true });
        return;
      }
      this.own.set(!username);
      this.load();
      this.title()?.nativeElement.focus();
    });
    this.destroyRef.onDestroy(() => {
      this.request?.unsubscribe();
      this.socialRequest?.unsubscribe();
      this.editor?.close();
    });
  }

  protected relationship(): RelationshipDto | null {
    const profile = this.profile();
    return profile && 'relationship' in profile ? profile.relationship : null;
  }

  protected showFriends(): boolean {
    const profile = this.profile();
    return !!profile && 'isRestricted' in profile && !profile.isRestricted;
  }

  protected refreshSocial(): void {
    this.socialRefresh.next();
  }

  private loadSocial(): void {
    if (!this.profile()) return;
    this.socialRequest?.unsubscribe();
    const username = this.route.snapshot.paramMap.get('username');
    const request: Observable<MyProfileDto | ProfileDto> = username
      ? this.users.getProfile(username)
      : this.users.getMe();
    this.socialRequest = request.subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.warning.set(null);
      },
      error: () =>
        this.warning.set('La acción se completó, pero no pudimos actualizar los datos del perfil.'),
    });
  }

  protected joined(date: string): string {
    return new Intl.DateTimeFormat('es-AR', { month: 'long', year: 'numeric' }).format(
      new Date(date),
    );
  }

  protected load(): void {
    if (this.cooldown.remaining()) {
      this.profile.set(null);
      this.loading.set(false);
      return;
    }
    this.request?.unsubscribe();
    this.loading.set(true);
    this.profile.set(null);
    this.error.set(null);
    this.notFound.set(false);
    const username = this.route.snapshot.paramMap.get('username');
    const request: Observable<MyProfileDto | ProfileDto> = username
      ? this.users.getProfile(username)
      : this.users.getMe();
    this.request = request.subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.loading.set(false);
      },
      error: (error) => {
        this.loading.set(false);
        if (this.cooldown.handle(error)) return;
        if (profileError(error).code === 'user_not_found') {
          this.notFound.set(true);
          return;
        }
        this.error.set('No pudimos cargar el perfil. Revisá tu conexión e intentá de nuevo.');
      },
    });
  }

  protected edit(origin: HTMLButtonElement): void {
    const profile = this.profile();
    if (!this.own() || !profile || !('id' in profile) || this.editor) return;
    this.editor = this.dialogs.open<ProfileEditResult, MyProfileDto, ProfileEditComponent>(
      ProfileEditComponent,
      {
        data: profile,
        ariaLabelledBy: 'profile-edit-title',
        ariaModal: true,
        backdropClass: 'cdk-overlay-dark-backdrop',
        autoFocus: 'first-tabbable',
        restoreFocus: origin,
        width: 'calc(100vw - 2rem)',
        maxWidth: '36rem',
      },
    );
    this.editor.closed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((result) => {
      this.editor = undefined;
      if (!result) return;
      this.profile.set(result.profile);
      this.status.set('Perfil actualizado');
      this.warning.set(result.warning);
      if (result.renamed) void this.router.navigate(['/profile'], { replaceUrl: true });
    });
  }

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.auth
      .logout()
      .pipe(
        finalize(() => {
          this.loggingOut.set(false);
          void this.router.navigateByUrl('/auth/login', { replaceUrl: true });
        }),
      )
      .subscribe();
  }
}
