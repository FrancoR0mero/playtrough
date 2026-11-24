import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

import { environment } from '../../../../environments/environment';

const COLORS = ['#57428a', '#315e6b', '#734556', '#476047', '#715029', '#454f86'];

@Component({
  selector: 'app-user-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display:inline-flex;flex-shrink:0;vertical-align:middle' },
  template: `
    @if (avatarUrl() && failedUrl() !== avatarUrl()) {
      <img
        [src]="imageUrl()"
        [alt]="label()"
        [width]="pixels()"
        [height]="pixels()"
        class="rounded-full object-cover"
        [style.width.px]="pixels()"
        [style.height.px]="pixels()"
        (error)="failedUrl.set(avatarUrl())"
      />
    } @else {
      <span
        role="img"
        [attr.aria-label]="label()"
        class="inline-flex items-center justify-center rounded-full font-semibold text-white"
        [style.width.px]="pixels()"
        [style.height.px]="pixels()"
        [style.font-size.px]="pixels() * 0.35"
        [style.background-color]="color()"
        >{{ initials() }}</span
      >
    }
  `,
})
export class UserAvatarComponent {
  readonly username = input.required<string>();
  readonly avatarUrl = input<string | null>(null);
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  protected readonly imageUrl = computed(() => {
    const url = this.avatarUrl();
    return url?.startsWith('/api/avatars/')
      ? new URL(url, new URL(environment.apiUrl, document.baseURI)).href
      : url;
  });
  protected readonly failedUrl = signal<string | null>(null);
  protected readonly pixels = computed(() => ({ sm: 32, md: 44, lg: 96 })[this.size()]);
  protected readonly label = computed(() => `Avatar de ${this.username()}`);
  protected readonly initials = computed(() =>
    Array.from(this.username()).slice(0, 2).join('').toUpperCase(),
  );
  protected readonly color = computed(() => {
    let hash = 0;
    for (const character of this.username().toLowerCase())
      hash = (hash * 31 + character.codePointAt(0)!) >>> 0;
    return COLORS[hash % COLORS.length];
  });
}
