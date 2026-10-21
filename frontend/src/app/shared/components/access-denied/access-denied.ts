import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-access-denied',
  imports: [RouterLink],
  templateUrl: './access-denied.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccessDeniedComponent {}
