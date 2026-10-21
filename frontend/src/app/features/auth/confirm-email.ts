import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from './services/auth';
import { ResendConfirmationComponent } from './resend-confirmation';

type ConfirmationState = 'loading' | 'success' | 'invalid' | 'retry';

@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink, ResendConfirmationComponent],
  templateUrl: './confirm-email.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmailComponent {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly email = this.route.snapshot.queryParamMap.get('email')?.trim() ?? '';
  // Kept only in memory for an explicit network retry, never persisted or logged.
  private token = this.route.snapshot.queryParamMap.get('token') ?? '';
  protected readonly state = signal<ConfirmationState>('loading');

  constructor() {
    void this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    if (!this.email || !this.token.trim()) this.state.set('invalid');
    else this.confirm();
  }

  protected retry(): void {
    if (this.state() !== 'retry') return;
    this.confirm();
  }

  private confirm(): void {
    this.state.set('loading');
    this.auth
      .confirmEmail(this.email, this.token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.token = '';
          this.state.set('success');
        },
        error: (error: unknown) => {
          if (error instanceof HttpErrorResponse && error.status === 400) {
            this.token = '';
            this.state.set('invalid');
          } else this.state.set('retry');
        },
      });
  }
}
