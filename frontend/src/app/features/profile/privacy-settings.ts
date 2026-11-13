import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProfilePrivacyComponent } from './profile-privacy';

@Component({
  selector: 'app-privacy-settings',
  imports: [RouterLink, ProfilePrivacyComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page-container" aria-labelledby="privacy-settings-title">
      <a
        routerLink="/profile"
        class="ui-button inline-flex min-h-11 items-center text-slate-300 hover:text-white"
        >Volver al perfil</a
      >
      <h1 id="privacy-settings-title" class="page-heading mt-4 text-white">
        Configuración de privacidad
      </h1>
      <p class="mt-3 max-w-2xl text-slate-400">
        Decidí quién puede ver tu perfil y tu biblioteca de juegos.
      </p>
      <a
        routerLink="/profile/settings/blocked"
        class="ui-button mt-5 inline-flex min-h-11 items-center border border-slate-600 px-4 py-2 text-white"
        >Usuarios bloqueados</a
      >
      <div class="max-w-2xl"><app-profile-privacy /></div>
    </section>
  `,
})
export class PrivacySettingsComponent {}
