import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Subject, tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import {
  AcceptedFriendDto,
  FriendDto,
  FriendRequestDto,
  FriendsPage,
  FriendsSummaryDto,
  FriendshipChange,
  SentFriendRequestDto,
} from '../models/friend';

@Injectable({ providedIn: 'root' })
export class FriendsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/friends`;
  private readonly changes = new Subject<FriendshipChange>();
  readonly changes$ = this.changes.asObservable();

  notifyBlockChange(username: string, action: 'blocked' | 'unblocked') {
    this.changes.next({ username, action });
  }

  summary() {
    return this.http.get<FriendsSummaryDto>(`${this.url}/summary`);
  }
  list(q = '', page = 1, pageSize = 20) {
    return this.http.get<FriendsPage<FriendDto>>(this.url, { params: { q, page, pageSize } });
  }
  received(page = 1, pageSize = 20) {
    return this.http.get<FriendsPage<FriendRequestDto>>(`${this.url}/requests/received`, {
      params: { page, pageSize },
    });
  }
  sent(page = 1, pageSize = 20) {
    return this.http.get<FriendsPage<FriendRequestDto>>(`${this.url}/requests/sent`, {
      params: { page, pageSize },
    });
  }
  forUser(username: string, page = 1, pageSize = 20) {
    return this.http.get<FriendsPage<FriendDto>>(
      `${environment.apiUrl}/users/${encodeURIComponent(username)}/friends`,
      { params: { page, pageSize } },
    );
  }
  send(username: string) {
    return this.http
      .post<SentFriendRequestDto>(`${this.url}/requests`, { username })
      .pipe(tap(() => this.changes.next({ username, action: 'sent' })));
  }
  accept(id: number) {
    return this.http
      .post<AcceptedFriendDto>(`${this.url}/requests/${id}/accept`, {})
      .pipe(
        tap((result) =>
          this.changes.next({ username: result.friend.username, action: 'accepted' }),
        ),
      );
  }
  reject(id: number, username: string) {
    return this.http
      .post<void>(`${this.url}/requests/${id}/reject`, {})
      .pipe(tap(() => this.changes.next({ username, action: 'rejected' })));
  }
  cancel(id: number, username: string) {
    return this.http
      .delete<void>(`${this.url}/requests/${id}`)
      .pipe(tap(() => this.changes.next({ username, action: 'cancelled' })));
  }
  remove(username: string) {
    return this.http
      .delete<void>(`${this.url}/${encodeURIComponent(username)}`)
      .pipe(tap(() => this.changes.next({ username, action: 'removed' })));
  }
}
