import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RoleChangeConfirmation } from './models/admin-users';

@Component({
  selector: 'app-confirm-role-change',
  templateUrl: './confirm-role-change.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmRoleChangeComponent {
  protected readonly change = inject<RoleChangeConfirmation>(DIALOG_DATA);
  protected readonly dialog = inject<DialogRef<boolean>>(DialogRef);
}
