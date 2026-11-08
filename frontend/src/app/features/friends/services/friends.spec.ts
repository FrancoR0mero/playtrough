import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { FRIEND, REQUEST, SUMMARY } from '../friends-test-helpers';
import { FriendsService } from './friends';

describe('FriendsService', () => {
  let service: FriendsService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/friends`;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FriendsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('loads summary and all paginated endpoints with encoded names and query parameters', () => {
    service.summary().subscribe((result) => expect(result).toEqual(SUMMARY));
    http.expectOne(`${url}/summary`).flush(SUMMARY);
    service.list('a & b', 2, 10).subscribe();
    const list = http.expectOne((req) => req.url === url);
    expect(list.request.params.get('q')).toBe('a & b');
    expect(list.request.params.get('page')).toBe('2');
    expect(list.request.params.get('pageSize')).toBe('10');
    list.flush({ items: [FRIEND], page: 2, pageSize: 10, hasMore: false });
    for (const direction of ['received', 'sent'] as const) {
      service[direction](3, 5).subscribe();
      const req = http.expectOne((r) => r.url === `${url}/requests/${direction}`);
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('page')).toBe('3');
      expect(req.request.params.get('pageSize')).toBe('5');
      req.flush({ items: [REQUEST], page: 3, pageSize: 5, hasMore: false });
    }
    service.forUser('a/b', 2, 6).subscribe();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/users/a%2Fb/friends`);
    expect(req.request.params.get('pageSize')).toBe('6');
    req.flush({ items: [], page: 2, pageSize: 6, hasMore: false });
  });
  it('sends every mutation to the correct endpoint and emits only successful changes', () => {
    const changed = vi.fn();
    service.changes$.subscribe(changed);
    service.send('bea').subscribe();
    const send = http.expectOne(`${url}/requests`);
    expect(send.request.method).toBe('POST');
    expect(send.request.body).toEqual({ username: 'bea' });
    send.flush({ ...REQUEST, status: 'pending' });
    service.accept(7).subscribe();
    const accept = http.expectOne(`${url}/requests/7/accept`);
    expect(accept.request.method).toBe('POST');
    accept.flush({ friend: FRIEND.user, since: FRIEND.since });
    service.reject(7, 'bea').subscribe();
    const reject = http.expectOne(`${url}/requests/7/reject`);
    expect(reject.request.method).toBe('POST');
    reject.flush(null);
    service.cancel(7, 'bea').subscribe();
    const cancel = http.expectOne(`${url}/requests/7`);
    expect(cancel.request.method).toBe('DELETE');
    cancel.flush(null);
    service.remove('a/b').subscribe();
    const remove = http.expectOne(`${url}/a%2Fb`);
    expect(remove.request.method).toBe('DELETE');
    remove.flush(null);
    expect(changed.mock.calls.map((call) => call[0].action)).toEqual([
      'sent',
      'accepted',
      'rejected',
      'cancelled',
      'removed',
    ]);
    service.send('bea').subscribe({ error: () => undefined });
    http.expectOne(`${url}/requests`).flush({}, { status: 409, statusText: 'Conflict' });
    expect(changed).toHaveBeenCalledTimes(5);
  });
});
