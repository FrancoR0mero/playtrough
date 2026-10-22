import { DestroyRef, inject, signal } from '@angular/core';

/** Create within an injection context; the timer belongs to the component's lifetime. */
export function createResendCooldown(seconds: number) {
  const remaining = signal(0);
  let timer: ReturnType<typeof setInterval> | undefined;
  inject(DestroyRef).onDestroy(() => clearInterval(timer));

  return {
    remaining: remaining.asReadonly(),
    start: (duration = seconds) => {
      clearInterval(timer);
      remaining.set(duration);
      if (duration <= 0) return;
      timer = setInterval(() => {
        remaining.update((value) => Math.max(0, value - 1));
        if (remaining() === 0) clearInterval(timer);
      }, 1000);
    },
  };
}
