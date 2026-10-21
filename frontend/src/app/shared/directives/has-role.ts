import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AppRole } from '../../core/authorization/roles';
import { AuthService } from '../../features/auth/services/auth';

/** Minimum-role visibility is UX only. The backend enforces authorization. */
@Directive({ selector: '[appHasRole]' })
export class HasRoleDirective {
  readonly minimumRole = input.required<AppRole>({ alias: 'appHasRole' });
  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private visible = false;

  constructor() {
    effect(() => {
      const allowed = this.auth.hasMinRole(this.minimumRole());
      if (allowed === this.visible) return;
      this.visible = allowed;
      if (allowed) this.container.createEmbeddedView(this.template);
      else this.container.clear();
    });
  }
}
