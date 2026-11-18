import { convertToParamMap } from '@angular/router';
import { parseTimelineFilters } from './timeline-filters';

describe('Timeline URL filters', () => {
  it('restores and canonicalizes combined filters without reading the cursor or tab', () => {
    expect(
      parseTimelineFilters(
        convertToParamMap({
          tab: 'timeline',
          cursor: 'ignored',
          year: '2026',
          month: '03',
          type: 'reviewed, added,reviewed',
        }),
        2026,
      ),
    ).toEqual({ year: 2026, month: 3, type: ['added', 'reviewed'] });
    expect(parseTimelineFilters(convertToParamMap({}), 2026)).toEqual({});
    expect(parseTimelineFilters(convertToParamMap({ month: '3' }), 2026)).toEqual({ month: 3 });
  });
  it.each([
    { year: '1969' },
    { year: '2028' },
    { year: 'NaN' },
    { year: '' },
    { month: '13' },
    { year: '2026', month: '0' },
    { year: '2026', month: '13' },
    { year: '2026', month: 'x' },
    { type: '' },
    { type: 'added,' },
    { type: 'other' },
    { year: ['2025', '2026'] },
    { type: ['added', 'reviewed'] },
  ])('rejects malformed filters: %j', (values) => {
    expect(parseTimelineFilters(convertToParamMap(values), 2026)).toBeNull();
  });
});
