export const LIBRARY_STATUSES = [
  {
    value: 'playing',
    label: 'Jugando',
    tabLabel: 'Jugando',
    style: 'border-sky-400/40 bg-sky-400/10 text-sky-200',
  },
  {
    value: 'backlog',
    label: 'Por jugar',
    tabLabel: 'Por jugar',
    style: 'border-amber-400/40 bg-amber-400/10 text-amber-200',
  },
  {
    value: 'beaten',
    label: 'Terminado',
    tabLabel: 'Terminados',
    style: 'border-emerald-400/40 bg-emerald-400/10 text-emerald-200',
  },
  {
    value: 'completed',
    label: 'Completado',
    tabLabel: 'Completados',
    style: 'border-violet-400/40 bg-violet-400/10 text-violet-200',
  },
] as const;

export type LibraryStatus = (typeof LIBRARY_STATUSES)[number]['value'];
export type LibrarySort = 'recent' | 'added' | 'name' | 'release';

export interface LibraryEntryDto {
  gameId: number;
  externalId: number;
  name: string;
  slug: string;
  coverUrl: string | null;
  releaseYear: number | null;
  status: LibraryStatus;
  statusChangedOn: string;
  addedAt: string;
}

export interface LibraryQuery {
  status?: LibraryStatus;
  q?: string;
  sort?: LibrarySort;
  page?: number;
  pageSize?: number;
}

export interface LibraryResponse {
  items: LibraryEntryDto[];
  counts: Record<LibraryStatus | 'total', number>;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export type LibraryChange =
  | { type: 'added' | 'status-changed'; gameId: number; entry: LibraryEntryDto }
  | { type: 'removed'; gameId: number };
