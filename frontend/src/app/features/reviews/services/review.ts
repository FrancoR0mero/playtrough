import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, map, tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  DeleteReviewResponse,
  MyReviewDto,
  ReviewChange,
  ReviewListResponse,
  ReviewSort,
  SaveReviewResponse,
} from '../models/review';

@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly http = inject(HttpClient);
  private readonly changes = new Subject<ReviewChange>();
  readonly changes$ = this.changes.asObservable();

  getMine(gameId: number): Observable<MyReviewDto> {
    return this.http.get<MyReviewDto>(`${this.url(gameId)}/me`);
  }

  save(gameId: number, rating: number, text?: string | null): Observable<SaveReviewResponse> {
    return this.http
      .put<SaveReviewResponse>(
        `${this.url(gameId)}/me`,
        { rating, ...(text === undefined ? {} : { text }) },
        { observe: 'response' },
      )
      .pipe(
        tap((response) =>
          this.changes.next({
            type: response.status === 201 ? 'created' : 'updated',
            gameId,
            ...response.body!,
          }),
        ),
        map((response) => response.body!),
      );
  }

  deleteMine(gameId: number): Observable<DeleteReviewResponse> {
    return this.http
      .delete<DeleteReviewResponse>(`${this.url(gameId)}/me`)
      .pipe(tap((response) => this.changes.next({ type: 'deleted', gameId, ...response })));
  }

  list(
    gameId: number,
    sort: ReviewSort,
    page: number,
    pageSize: number,
  ): Observable<ReviewListResponse> {
    const params = new HttpParams().set('sort', sort).set('page', page).set('pageSize', pageSize);
    return this.http.get<ReviewListResponse>(this.url(gameId), { params });
  }

  private url(gameId: number): string {
    return `${environment.apiUrl}/games/${gameId}/reviews`;
  }
}
