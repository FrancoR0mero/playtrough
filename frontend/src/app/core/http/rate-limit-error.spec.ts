import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { RateLimitError } from './rate-limit-error';

describe('RateLimitError', () => {
  it.each([
    ['3', 3],
    [' 90 ', 90],
    ['3600', 3600],
    [null, 60],
    ['', 60],
    ['invalid', 60],
    ['0', 60],
    ['-5', 60],
    ['1.5', 60],
    ['Infinity', 60],
    ['1e3', 60],
    ['9007199254740992', 60],
    ['Thu, 22 Oct 2026 12:00:00 GMT', 60],
  ])('reads positive integer seconds from %s, otherwise uses 60', (header, expected) => {
    const response = new HttpErrorResponse({
      status: 429,
      url: '/api/auth/login',
      error: { code: 'rate_limited' },
      headers: new HttpHeaders(header === null ? {} : { 'Retry-After': header }),
    });
    const error = new RateLimitError(response);
    expect(error).toBeInstanceOf(HttpErrorResponse);
    expect(error.retryAfterSeconds).toBe(expected);
    expect(error.status).toBe(429);
    expect(error.error).toEqual({ code: 'rate_limited' });
    expect(error.url).toBe(response.url);
    expect(error.headers).toBe(response.headers);
  });
});
