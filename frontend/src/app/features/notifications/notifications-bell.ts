import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { NotificationsState } from './notifications-state';

@Component({
  selector: 'app-notifications-bell',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a
      routerLink="/notifications"
      routerLinkActive="active"
      ariaCurrentWhenActive="page"
      [attr.aria-label]="unread() ? 'Notificaciones, sin leer: ' + unread() : 'Notificaciones'"
      class="bell-link"
    >
      <span class="bell-icon" aria-hidden="true"></span>
      @if (unread(); as count) {
        <span class="bell-count" aria-hidden="true">{{ count > 99 ? '99+' : count }}</span>
      }
    </a>
  `,
  styles: `
    .bell-link {
      display: grid;
      position: relative;
      place-items: center;
      width: 44px;
      height: 44px;
      color: var(--muted);
      border-radius: 9px;
    }
    .bell-link:hover,
    .bell-link.active {
      color: var(--text);
      background: var(--surface-raised);
    }
    .bell-icon {
      position: relative;
      width: 15px;
      height: 15px;
      border: 1.6px solid currentColor;
      border-radius: 8px 8px 3px 3px;
      margin-top: -3px;
    }
    .bell-icon::before,
    .bell-icon::after {
      position: absolute;
      content: '';
      bottom: -2px;
      height: 1.6px;
      left: -3px;
      right: -3px;
      background: currentColor;
      border-radius: 2px;
    }
    .bell-icon::after {
      bottom: -6px;
      left: 4px;
      right: 4px;
    }
    .bell-count {
      position: absolute;
      top: 2px;
      right: 0;
      min-width: 18px;
      padding: 0 3px;
      background: var(--color-accent);
      color: white;
      border-radius: 9px;
      font-size: 11px;
      line-height: 18px;
      text-align: center;
      font-weight: 650;
    }
  `,
})
export class NotificationsBellComponent {
  protected readonly unread = inject(NotificationsState).unreadCount;
}
