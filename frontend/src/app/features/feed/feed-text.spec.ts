import { effectiveFeedDate, feedTimestamp, fullFeedDate, relativeFeedTime } from './feed-text';

describe('Feed publication dates', () => {
  const now = Date.parse('2026-03-12T14:00:00Z');
  it.each([
    ['2026-03-12T12:00:00Z', 'hace 2 horas'],
    ['2026-03-12T13:58:00Z', 'hace 2 minutos'],
    ['2026-03-12T13:59:50Z', 'hace 10 segundos'],
    ['2026-03-10T14:00:00Z', 'anteayer'],
    ['2026-03-12T16:00:00Z', 'dentro de 2 horas'],
    ['2026-03-12T14:00:00Z', 'ahora'],
  ])('formats %s as %s', (date, text) => expect(relativeFeedTime(date, now)).toBe(text));
  it('treats timestamps without a suffix as UTC and preserves explicit offsets', () => {
    expect(feedTimestamp('2026-03-12T12:00:00')).toBe('2026-03-12T12:00:00.000Z');
    expect(feedTimestamp('2026-03-12T09:00:00-03:00')).toBe('2026-03-12T12:00:00.000Z');
    expect(fullFeedDate('2026-03-12T12:00:00Z')).toContain('2026');
    expect(effectiveFeedDate('2026-03-10')).toBe('10/03/2026');
  });
});
