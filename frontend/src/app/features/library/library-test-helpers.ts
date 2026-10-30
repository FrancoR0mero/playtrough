import { LibraryEntryDto, LibraryResponse } from './models/library';

export const EMPTY_LIBRARY: LibraryResponse = {
  items: [],
  counts: { playing: 0, backlog: 0, beaten: 0, completed: 0, abandoned: 0, total: 0 },
  page: 1,
  pageSize: 20,
  hasMore: false,
};

export const LIBRARY_GAMES: LibraryEntryDto[] = [
  {
    gameId: 17,
    externalId: 4200,
    name: 'Portal 2',
    slug: 'portal-2',
    coverUrl: null,
    releaseYear: 2011,
    status: 'playing',
    statusChangedOn: '2026-10-27',
    addedAt: '2026-10-27T12:00:00Z',
  },
  {
    gameId: 18,
    externalId: 123,
    name: 'Hades',
    slug: 'hades',
    coverUrl: null,
    releaseYear: 2020,
    status: 'backlog',
    statusChangedOn: '2026-10-26',
    addedAt: '2026-10-26T12:00:00Z',
  },
];
export const LIBRARY_COUNTS = {
  playing: 1,
  backlog: 1,
  beaten: 0,
  completed: 0,
  abandoned: 0,
  total: 2,
};
