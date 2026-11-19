import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

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

export type GroupDecision = 'approve' | 'reject';

export interface GroupPage<T> {
  items: T[];
  nextCursor: number | null;
}

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
    return this.http.get<GroupProfile>(`${this.url}/${encodeURIComponent(id)}`);
  }

  join(id: number) {
    return this.http.put<{ status: MembershipStatus }>(`${this.url}/${id}/membership`, null);
  }

  leave(id: number) {
    return this.http.delete<void>(`${this.url}/${id}/membership`);
  }

  members(id: number, cursor: number | null) {
    return this.peoplePage<GroupMember>(`${this.url}/${id}/members`, cursor);
  }

  joinRequests(id: number, cursor: number | null) {
    return this.peoplePage<GroupJoinRequest>(`${this.url}/${id}/join-requests`, cursor);
  }

  decide(id: number, requestId: number, decision: GroupDecision) {
    return this.http.post<void>(`${this.url}/${id}/join-requests/${requestId}/decision`, {
      decision,
    });
  }

  remove(id: number, userId: number) {
    return this.http.delete<void>(`${this.url}/${id}/members/${userId}`);
  }

  private peoplePage<T>(url: string, cursor: number | null) {
    return this.http.get<GroupPage<T>>(url, { params: cursor === null ? {} : { cursor } });
  }
}
