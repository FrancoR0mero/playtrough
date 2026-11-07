import { Injectable, signal } from '@angular/core';
import { rateLimitMessage } from '../rate-limit-message';

/** Lets a cancelled route guard display an alert on the page that remains open. */
@Injectable({ providedIn: 'root' })
export class RateLimitNoticeService {
  private readonly notice = signal<string | null>(null);
  readonly message = this.notice.asReadonly();

  show(seconds: number): void {
    this.notice.set(rateLimitMessage(seconds));
  }

  dismiss(): void {
    this.notice.set(null);
  }
}
