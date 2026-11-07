import { rateLimitMessage } from './rate-limit-message';

describe('rateLimitMessage', () => {
  it.each([
    [1, '1 segundo'],
    [30, '30 segundos'],
    [60, '60 segundos'],
    [61, '1 minuto y 1 segundo'],
    [90, '1 minuto y 30 segundos'],
    [120, '2 minutos'],
    [121, '2 minutos y 1 segundo'],
    [3600, '60 minutos'],
  ])('formats %s seconds consistently', (seconds, duration) => {
    expect(rateLimitMessage(seconds)).toBe(`Demasiados intentos. Probá de nuevo en ${duration}`);
  });
});
