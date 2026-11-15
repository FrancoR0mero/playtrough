import { TimelineEntryDto } from './models/timeline';

export function timelineEntry(overrides: Partial<TimelineEntryDto> = {}): TimelineEntryDto {
  return {
    id: 1,
    type: 'added',
    occurredOn: '2026-03-12',
    recordedAt: '2026-03-12T12:00:00Z',
    game: {
      gameId: 17,
      externalId: 4200,
      name: 'Portal 2',
      slug: 'portal-2',
      coverUrl: null,
      releaseYear: 2011,
    },
    fromStatus: null,
    toStatus: 'playing',
    review: null,
    isHidden: false,
    ...overrides,
  };
}
