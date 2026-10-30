import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { LibraryEntryDto } from '../models/library';
import { LibraryService } from './library';

const ENTRY: LibraryEntryDto = {
  gameId: 17,
  externalId: 4200,
  name: 'Portal 2',
  slug: 'portal-2',
  coverUrl: null,
  releaseYear: 2011,
  status: 'playing',
  statusChangedOn: '2026-10-27',
  addedAt: '2026-10-28T02:30:00Z',
};

describe('LibraryService', () => {
  let http: HttpTestingController;
  let service: LibraryService;
  const url = `${environment.apiUrl}/library`;
  let changed = vi.fn();

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(LibraryService);
    changed = vi.fn();
    service.changes$.subscribe(changed);
  });
  afterEach(() => {
    http.verify();
    vi.restoreAllMocks();
  });

  it('gets an entry using the local game ID without emitting a mutation', () => {
    const next = vi.fn();
    service.getEntry(17).subscribe(next);
    const request = http.expectOne(`${url}/games/17`);
    expect(request.request.method).toBe('GET');
    request.flush(ENTRY);
    expect(next).toHaveBeenCalledWith(ENTRY);
    expect(changed).not.toHaveBeenCalled();
  });

  it.each([undefined, '2026-10-27'])(
    'adds with optional date %s and emits only after success',
    (date) => {
      const next = vi.fn();
      service.add(17, 'playing', date).subscribe(next);
      const request = http.expectOne(url);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({
        gameId: 17,
        status: 'playing',
        ...(date ? { occurredOn: date } : {}),
      });
      expect(changed).not.toHaveBeenCalled();
      request.flush(ENTRY, { status: 201, statusText: 'Created' });
      expect(next).toHaveBeenCalledWith(ENTRY);
      expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'added', gameId: 17, entry: ENTRY });
    },
  );

  it.each([undefined, '2026-10-27'])(
    'updates status with optional date %s, including an unchanged status',
    (date) => {
      service.changeStatus(17, 'playing', date).subscribe();
      const request = http.expectOne(`${url}/17/status`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({
        status: 'playing',
        ...(date ? { occurredOn: date } : {}),
      });
      request.flush(ENTRY);
      expect(changed).toHaveBeenCalledExactlyOnceWith({
        type: 'status-changed',
        gameId: 17,
        entry: ENTRY,
      });
    },
  );

  it.each(['add', 'changeStatus'] as const)(
    '%s uses local components for an Argentine night crossing the UTC day',
    (method) => {
      const night = new Date('2026-10-27T23:30:00-03:00');
      // Model browser-local Argentina components independently of the test host's timezone.
      vi.spyOn(night, 'getFullYear').mockReturnValue(2026);
      vi.spyOn(night, 'getMonth').mockReturnValue(9);
      vi.spyOn(night, 'getDate').mockReturnValue(27);
      expect(night.getUTCDate()).toBe(28);
      const iso = vi.spyOn(night, 'toISOString');
      service[method](17, 'playing', night).subscribe();
      const request = http.expectOne(method === 'add' ? url : `${url}/17/status`);
      expect(request.request.body.occurredOn).toBe('2026-10-27');
      expect(iso).not.toHaveBeenCalled();
      request.flush(ENTRY);
    },
  );

  it('removes by local ID and emits once after 204', () => {
    service.remove(17).subscribe();
    const request = http.expectOne(`${url}/17`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toBeNull();
    request.flush(null, { status: 204, statusText: 'No Content' });
    expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'removed', gameId: 17 });
  });

  it.each(['add', 'changeStatus', 'remove'] as const)('does not emit on failed %s', (method) => {
    const error = vi.fn();
    const invalidated = vi.fn();
    service.invalidated$.subscribe(invalidated);
    const operation: Observable<LibraryEntryDto | void> =
      method === 'remove' ? service.remove(17) : service[method](17, 'playing');
    operation.subscribe({ error });
    http
      .expectOne(method === 'add' ? url : `${url}/17${method === 'changeStatus' ? '/status' : ''}`)
      .flush(
        { extensions: { code: 'concurrent_modification' } },
        { status: 409, statusText: 'Conflict' },
      );
    expect(error).toHaveBeenCalledOnce();
    expect(invalidated).toHaveBeenCalledExactlyOnceWith(error.mock.calls[0][0]);
    expect(changed).not.toHaveBeenCalled();
  });

  it('lists with encoded filters, sort and pagination and preserves global counts', () => {
    const next = vi.fn();
    service
      .list({ status: 'completed', q: 'Ratchet & Clank', sort: 'name', page: 2, pageSize: 12 })
      .subscribe(next);
    const request = http.expectOne((req) => req.url === url);
    expect(request.request.method).toBe('GET');
    expect(
      Object.fromEntries(
        request.request.params.keys().map((key) => [key, request.request.params.get(key)]),
      ),
    ).toEqual({
      status: 'completed',
      q: 'Ratchet & Clank',
      sort: 'name',
      page: '2',
      pageSize: '12',
    });
    expect(request.request.urlWithParams).toContain('q=Ratchet%20%26%20Clank');
    const response = {
      items: [],
      counts: { playing: 4, backlog: 3, beaten: 2, completed: 1, abandoned: 0, total: 10 },
      page: 2,
      pageSize: 12,
      hasMore: false,
    };
    request.flush(response);
    expect(next).toHaveBeenCalledWith(response);
    expect(changed).not.toHaveBeenCalled();
  });

  it('omits unspecified and empty filters', () => {
    service.list({ status: undefined, q: '' }).subscribe();
    const request = http.expectOne(url);
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ items: [] });
  });
});
