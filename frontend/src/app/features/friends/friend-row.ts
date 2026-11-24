import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UserAvatarComponent } from '../../shared/components/user-avatar/user-avatar';
import { UserSummaryDto } from '../profile/models/user';
import { profileDate } from '../profile/profile-error';

@Component({
  selector: 'app-friend-row',
  imports: [RouterLink, UserAvatarComponent],
  template: `
    <div class="flex flex-wrap items-center justify-between gap-4 py-5">
      <div class="min-w-0 flex-1 basis-48">
        <a
          [routerLink]="['/profile', user().username]"
          class="inline-flex min-h-11 max-w-full items-center gap-3 text-white hover:underline"
        >
          <app-user-avatar [username]="user().username" [avatarUrl]="user().avatarUrl" />
          <span class="min-w-0 break-words font-semibold">{{ user().username }}</span>
        </a>
        @if (since(); as date) {
          <p class="mt-2 text-sm text-slate-400">Amigos desde {{ formatDate(date) }}</p>
        }
      </div>
      <ng-content />
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FriendRowComponent {
  readonly user = input.required<UserSummaryDto>();
  readonly since = input<string | null>(null);
  protected readonly formatDate = profileDate;
}
