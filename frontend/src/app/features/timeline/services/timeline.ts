import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { CommentCountDto, CommentCreatedDto, CommentDto, CommentPage } from '../models/comment';
import {
  FeedEntryDto,
  TimelineEntryDto,
  TimelinePage,
  TimelinePeriods,
  TimelineQuery,
  TimelineType,
} from '../models/timeline';

@Injectable({ providedIn: 'root' })
export class TimelineService {
  private readonly http = inject(HttpClient);
  private readonly url = environment.apiUrl;

  getMine(query: TimelineQuery = {}) {
    return this.http.get<TimelinePage>(`${this.url}/timeline/me`, { params: this.params(query) });
  }
  getUser(username: string, query: TimelineQuery = {}) {
    return this.http.get<TimelinePage>(`${this.userUrl(username)}/timeline`, {
      params: this.params(query),
    });
  }
  getMyPeriods(type?: readonly TimelineType[]) {
    return this.http.get<TimelinePeriods>(`${this.url}/timeline/me/periods`, {
      params: this.params({ type }),
    });
  }
  getUserPeriods(username: string, type?: readonly TimelineType[]) {
    return this.http.get<TimelinePeriods>(`${this.userUrl(username)}/timeline/periods`, {
      params: this.params({ type }),
    });
  }
  setVisibility(id: number, hidden: boolean) {
    return this.http.put<TimelineEntryDto>(`${this.url}/timeline/entries/${id}/visibility`, {
      hidden,
    });
  }
  getFeed(query: Pick<TimelineQuery, 'cursor' | 'limit'> = {}) {
    return this.http.get<TimelinePage<FeedEntryDto>>(`${this.url}/feed`, {
      params: this.params(query),
    });
  }
  getEntry(id: number) {
    return this.http.get<FeedEntryDto>(this.entryUrl(id));
  }
  getComments(entryId: number, query: Pick<TimelineQuery, 'cursor' | 'limit'> = {}) {
    return this.http.get<CommentPage>(`${this.entryUrl(entryId)}/comments`, {
      params: this.params(query),
    });
  }
  addComment(entryId: number, text: string) {
    return this.http.post<CommentCreatedDto>(`${this.entryUrl(entryId)}/comments`, { text });
  }
  editComment(id: number, text: string) {
    return this.http.put<CommentDto>(this.commentUrl(id), { text });
  }
  deleteComment(id: number) {
    return this.http.delete<CommentCountDto>(this.commentUrl(id));
  }
  private entryUrl(id: number) {
    return `${this.url}/timeline/entries/${id}`;
  }
  private commentUrl(id: number) {
    return `${this.url}/timeline/comments/${id}`;
  }
  private userUrl(username: string) {
    return `${this.url}/users/${encodeURIComponent(username)}`;
  }
  private params(query: TimelineQuery): HttpParams {
    let params = new HttpParams();
    for (const key of ['cursor', 'limit', 'year', 'month'] as const) {
      const value = query[key];
      if (value !== undefined) params = params.set(key, value);
    }
    if (query.type?.length) params = params.set('type', query.type.join(','));
    return params;
  }
}
