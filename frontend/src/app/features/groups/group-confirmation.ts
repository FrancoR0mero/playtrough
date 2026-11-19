import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ConfirmationContentComponent } from '../../shared/components/confirmation/confirmation-content';

export interface GroupConfirmation {
  title: string;
  message: string;
  label: string;
}

@Component({
  selector: 'app-group-confirmation',
  imports: [ConfirmationContentComponent],
  template: `
    <app-confirmation-content
      eyebrow="Grupos"
      [title]="data.title"
      [confirmLabel]="data.label"
      titleId="group-confirmation-title"
      descriptionId="group-confirmation-description"
      (confirmed)="dialog.close(true)"
      (cancelled)="dialog.close()"
      >{{ data.message }}</app-confirmation-content
    >
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GroupConfirmationComponent {
  protected readonly data = inject<GroupConfirmation>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<boolean>>(DialogRef);
}
