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

interface GroupPage {
  items: SocialGroup[];
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
    return this.http.get<GroupPage>(this.url, { params });
  }

  create(group: SaveGroup) {
    return this.http.post<SocialGroup>(this.url, group);
  }

  get(id: string) {
    return this.http.get<SocialGroup>(`${this.url}/${encodeURIComponent(id)}`);
  }
}
