import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConfirmationContentComponent } from '../../shared/components/confirmation/confirmation-content';

@Component({
  selector: 'app-confirm-library-removal',
  imports: [ConfirmationContentComponent],
  template: `
    <app-confirmation-content
      eyebrow="MI BIBLIOTECA"
      title="Quitar de mi biblioteca"
      titleId="library-removal-title"
      descriptionId="library-removal-description"
      confirmLabel="Quitar"
      (cancelled)="dialog.close(false)"
      (confirmed)="dialog.close(true)"
    >
      ¿Quitar <strong class="text-white">{{ name }}</strong> de tu biblioteca?
    </app-confirmation-content>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmLibraryRemovalComponent {
  protected readonly name = inject<string>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<boolean>>(DialogRef);
}
