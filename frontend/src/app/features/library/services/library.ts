import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, Subject, tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { localCalendarDate } from '../library-date';
import {
  LibraryChange,
  LibraryEntryDto,
  LibraryQuery,
  LibraryResponse,
  LibraryStatus,
} from '../models/library';

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/library`;
  private readonly changes = new Subject<LibraryChange>();
  readonly changes$ = this.changes.asObservable();

  getEntry(gameId: number): Observable<LibraryEntryDto> {
    return this.http.get<LibraryEntryDto>(`${this.url}/games/${gameId}`);
  }

  add(
    gameId: number,
    status: LibraryStatus,
    occurredOn?: string | Date,
  ): Observable<LibraryEntryDto> {
    return this.http
      .post<LibraryEntryDto>(this.url, { gameId, ...this.statusBody(status, occurredOn) })
      .pipe(tap((entry) => this.changes.next({ type: 'added', gameId, entry })));
  }

  changeStatus(
    gameId: number,
    status: LibraryStatus,
    occurredOn?: string | Date,
  ): Observable<LibraryEntryDto> {
    return this.http
      .put<LibraryEntryDto>(`${this.url}/${gameId}/status`, this.statusBody(status, occurredOn))
      .pipe(tap((entry) => this.changes.next({ type: 'status-changed', gameId, entry })));
  }

  remove(gameId: number): Observable<void> {
    return this.http
      .delete<void>(`${this.url}/${gameId}`)
      .pipe(tap(() => this.changes.next({ type: 'removed', gameId })));
  }

  list(query: LibraryQuery = {}): Observable<LibraryResponse> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') params = params.set(key, value);
    }
    return this.http.get<LibraryResponse>(this.url, { params });
  }

  private statusBody(status: LibraryStatus, occurredOn?: string | Date) {
    return {
      status,
      ...(occurredOn === undefined
        ? {}
        : {
            occurredOn: occurredOn instanceof Date ? localCalendarDate(occurredOn) : occurredOn,
          }),
    };
  }
}
