import { HttpErrorResponse } from '@angular/common/http';

/** A temporary API rejection, independent of authentication and account lockout. */
export class RateLimitError extends HttpErrorResponse {
  readonly retryAfterSeconds: number;

  constructor(response: HttpErrorResponse) {
    super({
      error: response.error,
      headers: response.headers,
      status: response.status,
      statusText: response.statusText,
      url: response.url ?? undefined,
    });
    const header = response.headers.get('Retry-After')?.trim() ?? '';
    const seconds = /^\d+$/.test(header) ? Number(header) : NaN;
    this.retryAfterSeconds = Number.isSafeInteger(seconds) && seconds > 0 ? seconds : 60;
  }
}
