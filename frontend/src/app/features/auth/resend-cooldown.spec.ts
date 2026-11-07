import { TestBed } from '@angular/core/testing';
import { createResendCooldown } from './resend-cooldown';

describe('Reusable resend countdown', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });

  it('accepts server seconds and returns to the existing default on the next start', () => {
    const cooldown = TestBed.runInInjectionContext(() => createResendCooldown(30));
    cooldown.start(3);
    expect(cooldown.remaining()).toBe(3);
    vi.advanceTimersByTime(2_000);
    expect(cooldown.remaining()).toBe(1);
    vi.advanceTimersByTime(1_000);
    expect(cooldown.remaining()).toBe(0);
    expect(vi.getTimerCount()).toBe(0);
    cooldown.start();
    expect(cooldown.remaining()).toBe(30);
  });

  it('replaces the active timer and cleans it up when its owner is destroyed', () => {
    const cooldown = TestBed.runInInjectionContext(() => createResendCooldown(30));
    cooldown.start(3);
    vi.advanceTimersByTime(1_000);
    cooldown.start(10);
    vi.advanceTimersByTime(1_000);
    expect(cooldown.remaining()).toBe(9);
    TestBed.resetTestingModule();
    expect(vi.getTimerCount()).toBe(0);
  });
});
