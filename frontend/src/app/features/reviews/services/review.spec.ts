import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { MyReviewDto, ReviewListResponse, ReviewSort } from '../models/review';
import { ReviewService } from './review';

const REVIEW: MyReviewDto = {
  id: 3,
  gameId: 17,
  rating: 3.5,
  text: 'Buen juego',
  createdAt: '2026-11-03T12:00:00Z',
  editedAt: null,
};
const SUMMARY = { average: 4.3, count: 12 };

describe('ReviewService', () => {
  let http: HttpTestingController;
  let service: ReviewService;
  let changed = vi.fn();
  const url = `${environment.apiUrl}/games/17/reviews`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(ReviewService);
    changed = vi.fn();
    service.changes$.subscribe(changed);
  });
  afterEach(() => http.verify());

  it('gets my review by local game ID without emitting changes', () => {
    const next = vi.fn();
    service.getMine(17).subscribe(next);
    const req = http.expectOne(`${url}/me`);
    expect(req.request.method).toBe('GET');
    req.flush(REVIEW);
    expect(next).toHaveBeenCalledExactlyOnceWith(REVIEW);
    expect(changed).not.toHaveBeenCalled();
  });

  it.each([
    [201, 'created'],
    [200, 'updated'],
  ] as const)('saves with HTTP %s and emits %s with the returned summary', (status, type) => {
    const next = vi.fn();
    const response = { review: REVIEW, ratingSummary: SUMMARY };
    service.save(17, 3.5, 'Buen juego').subscribe(next);
    const req = http.expectOne(`${url}/me`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ rating: 3.5, text: 'Buen juego' });
    expect(changed).not.toHaveBeenCalled();
    req.flush(response, { status, statusText: status === 201 ? 'Created' : 'OK' });
    expect(next).toHaveBeenCalledExactlyOnceWith(response);
    expect(changed).toHaveBeenCalledExactlyOnceWith({ type, gameId: 17, ...response });
  });

  it.each([undefined, null, '', '<b>hola</b>\n<script>alert(1)</script>'])(
    'preserves optional plain text %s',
    (text) => {
      service.save(17, 5, text).subscribe();
      const req = http.expectOne(`${url}/me`);
      expect(req.request.body).toEqual({ rating: 5, ...(text === undefined ? {} : { text }) });
      req.flush({ review: { ...REVIEW, rating: 5, text: text ?? null }, ratingSummary: SUMMARY });
    },
  );

  it('deletes my review and emits the returned empty summary', () => {
    const next = vi.fn();
    const response = { ratingSummary: { average: null, count: 0 } };
    service.deleteMine(17).subscribe(next);
    const req = http.expectOne(`${url}/me`);
    expect(req.request.method).toBe('DELETE');
    expect(changed).not.toHaveBeenCalled();
    req.flush(response);
    expect(next).toHaveBeenCalledExactlyOnceWith(response);
    expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'deleted', gameId: 17, ...response });
  });

  it.each<ReviewSort>(['recent', 'top', 'low'])(
    'lists with sort %s and explicit pagination',
    (sort) => {
      const next = vi.fn();
      const response: ReviewListResponse = {
        items: [
          {
            id: REVIEW.id,
            rating: REVIEW.rating,
            text: REVIEW.text,
            createdAt: REVIEW.createdAt,
            editedAt: REVIEW.editedAt,
            author: { userId: 8, username: 'player' },
            isMine: true,
          },
        ],
        ratingSummary: SUMMARY,
        page: 2,
        pageSize: 10,
        hasMore: true,
      };
      service.list(17, sort, 2, 10).subscribe(next);
      const req = http.expectOne((request) => request.url === url);
      expect(req.request.method).toBe('GET');
      expect(req.request.params.keys()).toEqual(['sort', 'page', 'pageSize']);
      expect(req.request.params.get('sort')).toBe(sort);
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush(response);
      expect(next).toHaveBeenCalledExactlyOnceWith(response);
      expect(changed).not.toHaveBeenCalled();
    },
  );

  it.each(['get', 'save', 'delete', 'list'] as const)(
    'propagates %s errors unchanged without a success event',
    (method) => {
      const error = vi.fn();
      const operation: Observable<unknown> =
        method === 'get'
          ? service.getMine(17)
          : method === 'save'
            ? service.save(17, 4)
            : method === 'delete'
              ? service.deleteMine(17)
              : service.list(17, 'recent', 1, 10);
      operation.subscribe({ error });
      const req = http.expectOne(
        (request) => request.url === (method === 'list' ? url : `${url}/me`),
      );
      const body = {
        extensions: { code: method === 'save' ? 'concurrent_modification' : 'review_not_found' },
      };
      req.flush(body, { status: method === 'save' ? 409 : 404, statusText: 'Error' });
      expect(error).toHaveBeenCalledOnce();
      expect(error.mock.calls[0][0].error).toEqual(body);
      expect(changed).not.toHaveBeenCalled();
    },
  );
});
