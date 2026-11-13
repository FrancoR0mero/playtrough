import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { FriendsService } from '../../friends/services/friends';
import { UserSummaryDto } from '../../profile/models/user';

export interface BlockedUser {
  user: UserSummaryDto;
  blockedAt: string;
}
export interface BlockedUsersPage {
  items: BlockedUser[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

@Injectable({ providedIn: 'root' })
export class BlocksService {
  private readonly http = inject(HttpClient);
  private readonly friends = inject(FriendsService);
  private readonly url = `${environment.apiUrl}/blocks`;
  list(page = 1, pageSize = 20) {
    return this.http.get<BlockedUsersPage>(this.url, { params: { page, pageSize } });
  }
  block(username: string) {
    return this.http
      .post<void>(this.url, { username })
      .pipe(tap(() => this.friends.notifyBlockChange(username, 'blocked')));
  }
  unblock(username: string) {
    return this.http
      .delete<void>(`${this.url}/${encodeURIComponent(username)}`)
      .pipe(tap(() => this.friends.notifyBlockChange(username, 'unblocked')));
  }
}
