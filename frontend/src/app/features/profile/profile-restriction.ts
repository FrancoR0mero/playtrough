import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ProfileRestriction } from './models/user';

@Component({
  selector: 'app-profile-restriction',
  template: `
    <p role="alert" class="mt-6 flex items-start gap-3 text-sm leading-relaxed text-slate-300">
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1.5"
        aria-hidden="true"
        class="h-5 w-5 shrink-0"
      >
        <rect x="5" y="10" width="14" height="11" rx="2" />
        <path d="M8 10V7a4 4 0 0 1 8 0v3M12 14v3" />
      </svg>
      <span>{{
        restriction() === 'private'
          ? 'Este perfil es privado'
          : restriction() === 'friends_only'
            ? 'El contenido de este perfil es solo para amigos'
            : 'Este perfil tiene acceso restringido'
      }}</span>
    </p>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfileRestrictionComponent {
  readonly restriction = input<ProfileRestriction | null>(null);
}
