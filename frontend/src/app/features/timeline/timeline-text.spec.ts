import { LibraryStatus } from '../library/models/library';
import { TimelinePerspective } from './models/timeline';
import { describeEntry, timelineDate } from './timeline-text';
import { timelineEntry } from './timeline-test-helpers';

describe('describeEntry', () => {
  const statusTexts: Record<LibraryStatus, readonly [string, string]> = {
    playing: ['Empezaste a jugar a', 'empezó a jugar a'],
    backlog: ['Agregaste al backlog', 'agregó al backlog'],
    beaten: ['Terminaste', 'terminó'],
    completed: ['Completaste al 100 %', 'completó al 100 %'],
    abandoned: ['Abandonaste', 'abandonó'],
  };
  for (const perspective of ['self', 'other'] as TimelinePerspective[]) {
    const index = perspective === 'self' ? 0 : 1;
    it.each(Object.keys(statusTexts) as LibraryStatus[])(
      'describes added %s in ' + perspective,
      (toStatus) => {
        expect(describeEntry(timelineEntry({ toStatus }), perspective)).toBe(
          `${statusTexts[toStatus][index]} Portal 2`,
        );
      },
    );
    for (const fromStatus of [
      null,
      'playing',
      'backlog',
      'beaten',
      'completed',
      'abandoned',
    ] as const) {
      it.each(Object.keys(statusTexts) as LibraryStatus[])(
        `describes ${fromStatus} -> %s in ${perspective}`,
        (toStatus) => {
          const expected =
            toStatus === 'backlog'
              ? ['Mandaste al backlog', 'mandó al backlog']
              : toStatus === 'playing' &&
                  ['beaten', 'completed', 'abandoned'].includes(fromStatus ?? '')
                ? ['Retomaste', 'retomó']
                : statusTexts[toStatus];
          expect(
            describeEntry(
              timelineEntry({ type: 'status_changed', fromStatus, toStatus }),
              perspective,
            ),
          ).toBe(`${expected[index]} Portal 2`);
        },
      );
    }
    it('describes a review in ' + perspective, () => {
      expect(describeEntry(timelineEntry({ type: 'reviewed', toStatus: null }), perspective)).toBe(
        `${index === 0 ? 'Reseñaste' : 'reseñó'} Portal 2`,
      );
    });
    it('handles missing status in ' + perspective, () => {
      expect(describeEntry(timelineEntry({ toStatus: null }), perspective)).toContain(
        index === 0 ? 'Agregaste a tu biblioteca' : 'agregó a su biblioteca',
      );
      expect(
        describeEntry(timelineEntry({ type: 'status_changed', toStatus: null }), perspective),
      ).toContain(index === 0 ? 'Cambiaste el estado de' : 'cambió el estado de');
    });
  }
  it('keeps calendar dates independent from the timezone and formats months', () => {
    expect(timelineDate('2026-03-12')).toBe('12 de marzo de 2026');
    expect(timelineDate('2026-03-01', true)).toBe('Marzo de 2026');
    expect(timelineDate('2026-01-01')).toBe('1 de enero de 2026');
  });
});
