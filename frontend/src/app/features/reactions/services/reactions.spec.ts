import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { ReactionTarget } from '../models/reaction';
import { ReactionsService } from './reactions';

describe('ReactionsService', () => {
  const paths: [ReactionTarget, string][] = [
    [{ type: 'entry', id: 42 }, 'timeline/entries/42'],
    [{ type: 'comment', id: 8 }, 'timeline/comments/8'],
    [{ type: 'review', gameId: 7, id: 8 }, 'games/7/reviews/8'],
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it.each(paths)('uses the reaction endpoint for %j', (target, path) => {
    const service = TestBed.inject(ReactionsService);
    const http = TestBed.inject(HttpTestingController);
    const url = `${environment.apiUrl}/${path}/reaction`;
    const saved = vi.fn();
    const removed = vi.fn();
    const next = { myReaction: 'dislike', likeCount: 3, dislikeCount: 6 };
    service.set(target, 'dislike').subscribe(saved);
    const put = http.expectOne({ method: 'PUT', url });
    expect(put.request.body).toEqual({ value: 'dislike' });
    put.flush(next);
    expect(saved).toHaveBeenCalledExactlyOnceWith(next);
    service.remove(target).subscribe(removed);
    const del = http.expectOne({ method: 'DELETE', url });
    expect(del.request.body).toBeNull();
    const none = { myReaction: null, likeCount: 3, dislikeCount: 5 };
    del.flush(none);
    expect(removed).toHaveBeenCalledExactlyOnceWith(none);
  });
});
