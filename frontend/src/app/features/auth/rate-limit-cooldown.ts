import { computed } from '@angular/core';
import { RateLimitError } from '../../core/http/rate-limit-error';
import { rateLimitMessage } from '../../shared/rate-limit-message';
import { createResendCooldown } from './resend-cooldown';

/** A countdown for one form. It never retries a request or persists a client limit. */
export function createRateLimitCooldown() {
  const cooldown = createResendCooldown(0);
  return {
    remaining: cooldown.remaining,
    message: computed(() =>
      cooldown.remaining() > 0 ? rateLimitMessage(cooldown.remaining()) : null,
    ),
    handle(error: unknown): boolean {
      if (!(error instanceof RateLimitError)) return false;
      cooldown.start(error.retryAfterSeconds);
      return true;
    },
  };
}
