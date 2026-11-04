import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../features/auth/services/auth';
import { HasRoleDirective } from '../../directives/has-role';
import { NAVIGATION_ITEMS } from './layout-navigation';
import { RateLimitNoticeService } from '../../services/rate-limit-notice';
import { SiteFooterComponent } from './site-footer';

@Component({
  selector: 'app-layout',
  host: { '(keydown.escape)': 'closeMenu(true)' },
  imports: [RouterOutlet, RouterLink, RouterLinkActive, HasRoleDirective, SiteFooterComponent],
  templateUrl: './layout.html',
  styleUrl: './layout.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutComponent {
  protected readonly auth = inject(AuthService);
  protected readonly rateLimitNotice = inject(RateLimitNoticeService);
  protected readonly loggingOut = signal(false);
  protected readonly menuOpen = signal(false);
  protected readonly navigationItems = NAVIGATION_ITEMS;
  private readonly menuToggle = viewChild<ElementRef<HTMLButtonElement>>('menuToggle');
  private readonly router = inject(Router);

  protected closeMenu(restoreFocus = false): void {
    const wasOpen = this.menuOpen();
    this.menuOpen.set(false);
    if (restoreFocus && wasOpen) this.menuToggle()?.nativeElement.focus();
  }

  protected logout(): void {
    if (this.loggingOut()) return;
    this.closeMenu();
    this.loggingOut.set(true);
    this.auth
      .logout()
      .pipe(
        finalize(() => {
          this.loggingOut.set(false);
          // AuthService has already cleared the session. Start a fresh login without returnUrl.
          void this.router.navigateByUrl('/auth/login', { replaceUrl: true });
        }),
      )
      .subscribe();
  }
}
