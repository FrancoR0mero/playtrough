import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../features/auth/services/auth';

@Component({
  selector: 'app-layout',
  imports: [RouterOutlet],
  templateUrl: './layout.html',
  styleUrl: './layout.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LayoutComponent {
  protected readonly auth = inject(AuthService);
  protected readonly loggingOut = signal(false);

  protected logout(): void {
    if (this.loggingOut()) return;
    this.loggingOut.set(true);
    this.auth
      .logout()
      .pipe(finalize(() => this.loggingOut.set(false)))
      .subscribe();
  }
}
