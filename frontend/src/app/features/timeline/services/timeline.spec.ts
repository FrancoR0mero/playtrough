import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { TimelineService } from './timeline';
import { timelineEntry } from '../timeline-test-helpers';

describe('TimelineService', () => {
  let service: TimelineService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TimelineService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it('gets own activity with opaque cursor and combined filters', () => {
    service
      .getMine({ cursor: 'v1.ab-_', limit: 2, year: 2026, month: 3, type: ['added', 'reviewed'] })
      .subscribe();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/timeline/me`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual(['cursor', 'limit', 'year', 'month', 'type']);
    expect(req.request.params.get('cursor')).toBe('v1.ab-_');
    expect(req.request.params.get('limit')).toBe('2');
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.get('month')).toBe('3');
    expect(req.request.params.get('type')).toBe('added,reviewed');
    req.flush({ items: [], nextCursor: null, hasMore: false });
  });
  it('omits absent filters and cursors on a first page', () => {
    service.getMine().subscribe();
    const req = http.expectOne(`${environment.apiUrl}/timeline/me`);
    expect(req.request.params.keys()).toEqual([]);
    req.flush({ items: [], nextCursor: null, hasMore: false });
  });
  it('encodes usernames as one path segment and sends user filters', () => {
    service.getUser('ana/a', { year: 2026, type: ['status_changed'] }).subscribe();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/users/ana%2Fa/timeline`);
    expect(req.request.params.get('year')).toBe('2026');
    expect(req.request.params.get('type')).toBe('status_changed');
    req.flush({ items: [] });
  });
  it('gets own and user periods with only the selected types', () => {
    service.getMyPeriods(['added', 'reviewed']).subscribe();
    const mine = http.expectOne(`${environment.apiUrl}/timeline/me/periods?type=added,reviewed`);
    expect(mine.request.method).toBe('GET');
    mine.flush({ years: [] });
    service.getUserPeriods('ana').subscribe();
    http.expectOne(`${environment.apiUrl}/users/ana/timeline/periods`).flush({ years: [] });
  });
  it.each([true, false])('changes only the visibility to %s', (hidden) => {
    service.setVisibility(42, hidden).subscribe((entry) => expect(entry.isHidden).toBe(hidden));
    const req = http.expectOne(`${environment.apiUrl}/timeline/entries/42/visibility`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ hidden });
    req.flush(timelineEntry({ isHidden: hidden }));
  });
  it('gets feed cursor and limit without transforming the cursor', () => {
    service.getFeed({ cursor: 'v1.ab-_', limit: 7 }).subscribe();
    const req = http.expectOne((r) => r.url === `${environment.apiUrl}/feed`);
    expect(req.request.params.get('cursor')).toBe('v1.ab-_');
    expect(req.request.params.get('limit')).toBe('7');
    req.flush({ items: [], nextCursor: null, hasMore: false });
  });
});
