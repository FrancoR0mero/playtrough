import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/services/auth';

@Component({
  selector: 'app-library',
  imports: [RouterLink],
  templateUrl: './library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryComponent {
  protected readonly auth = inject(AuthService);
}
