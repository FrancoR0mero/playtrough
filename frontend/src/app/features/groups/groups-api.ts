import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import type { ChatMessage } from './group-chat-connection';

export type GroupPrivacy = 'public' | 'private';

export interface SaveGroup {
  name: string;
  description: string;
  topic: string;
  privacy: GroupPrivacy;
}

export interface SocialGroup extends SaveGroup {
  id: number;
}

export type MembershipStatus = 'owner' | 'member' | 'pending' | 'rejected' | 'removed' | 'none';

export interface GroupProfile extends SocialGroup {
  membership: MembershipStatus;
  version: string | null;
}

export interface GroupMember {
  userId: number;
  username: string;
  avatarUrl: string | null;
  joinedAt: string;
  isOwner: boolean;
}

export interface GroupJoinRequest {
  id: number;
  userId: number;
  username: string;
  avatarUrl: string | null;
  createdAt: string;
}

export interface GroupReview {
  id: number;
  gameId: number;
  gameName: string;
  username: string;
  rating: number;
  text: string | null;
  createdAt: string;
  editedAt: string | null;
}

export type GroupDecision = 'approve' | 'reject';

export interface GroupPage<T> {
  items: T[];
  nextCursor: number | null;
}

export interface ChatHistoryPage {
  items: ChatMessage[];
  nextCursor: string | null;
  through: string | null;
}

export type ChatHistoryQuery = { before: string } | { after: string; through?: string };

@Injectable({ providedIn: 'root' })
export class GroupsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/groups`;

  topics() {
    return this.http.get<string[]>(`${this.url}/topics`);
  }

  list(q: string, topic: string, cursor: number | null) {
    let params = new HttpParams();
    if (q) params = params.set('q', q);
    if (topic) params = params.set('topic', topic);
    if (cursor !== null) params = params.set('cursor', cursor);
    return this.http.get<GroupPage<SocialGroup>>(this.url, { params });
  }

  create(group: SaveGroup) {
    return this.http.post<SocialGroup>(this.url, group);
  }

  get(id: string) {
    return this.http
      .get<Omit<GroupProfile, 'version'>>(`${this.url}/${encodeURIComponent(id)}`, {
        observe: 'response',
      })
      .pipe(
        map((response): GroupProfile => ({
          ...response.body!,
          version: response.headers.get('ETag'),
        })),
      );
  }

  update(id: number, group: SaveGroup, version: string) {
    return this.http.put<void>(`${this.url}/${id}`, group, { headers: { 'If-Match': version } });
  }

  deleteGroup(id: number, version: string) {
    return this.http.delete<void>(`${this.url}/${id}`, { headers: { 'If-Match': version } });
  }

  join(id: number) {
    return this.http.put<{ status: MembershipStatus }>(`${this.url}/${id}/membership`, null);
  }

  leave(id: number) {
    return this.http.delete<void>(`${this.url}/${id}/membership`);
  }

  members(id: number, cursor: number | null) {
    return this.page<GroupMember>(`${this.url}/${id}/members`, cursor);
  }

  joinRequests(id: number, cursor: number | null) {
    return this.page<GroupJoinRequest>(`${this.url}/${id}/join-requests`, cursor);
  }

  reviews(id: number, cursor: number | null) {
    return this.page<GroupReview>(`${this.url}/${id}/reviews`, cursor);
  }

  chatHistory(id: number, query?: ChatHistoryQuery) {
    let params = new HttpParams();
    if (query && 'before' in query) params = params.set('before', query.before);
    if (query && 'after' in query) {
      params = params.set('after', query.after);
      if (query.through !== undefined) params = params.set('through', query.through);
    }
    return this.http.get<ChatHistoryPage>(`${this.url}/${id}/messages`, { params });
  }

  decide(id: number, requestId: number, decision: GroupDecision) {
    return this.http.post<void>(`${this.url}/${id}/join-requests/${requestId}/decision`, {
      decision,
    });
  }

  remove(id: number, userId: number) {
    return this.http.delete<void>(`${this.url}/${id}/members/${userId}`);
  }

  private page<T>(url: string, cursor: number | null) {
    return this.http.get<GroupPage<T>>(url, { params: cursor === null ? {} : { cursor } });
  }
}
