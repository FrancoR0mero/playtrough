import { displayLibraryDate, localCalendarDate } from './library-date';

describe('Library calendar dates', () => {
  it('pads local month and day without UTC conversion', () => {
    expect(localCalendarDate(new Date(2026, 0, 2, 23, 30))).toBe('2026-01-02');
  });
  it('formats calendar strings without shifting the day', () => {
    expect(displayLibraryDate('2026-10-27')).toBe('27/10/2026');
  });
});
