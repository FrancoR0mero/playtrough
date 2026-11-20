import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface NotificationItem {
  id: string;
  createdAt: string;
  readAt: string | null;
  message: string;
  destination: string | null;
}

export interface NotificationPage {
  items: NotificationItem[];
  nextCursor: string | null;
  readCutoff: string | null;
}

export interface NotificationPreference {
  category: string;
  enabled: boolean;
  editable: boolean;
}

interface NotificationCount {
  unreadCount: number;
}

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/notifications`;

  list(cursor?: string) {
    return this.http.get<NotificationPage>(this.url, { params: cursor ? { cursor } : {} });
  }

  count() {
    return this.http.get<NotificationCount>(`${this.url}/unread-count`);
  }

  read(id: string) {
    return this.http.put<void>(`${this.url}/${encodeURIComponent(id)}/read`, null);
  }

  readAll(cutoff: string) {
    return this.http.post<NotificationCount>(`${this.url}/read-all`, { cutoff });
  }

  preferences() {
    return this.http.get<NotificationPreference[]>(`${this.url}/preferences`);
  }

  setPreference(category: string, enabled: boolean) {
    return this.http.put<NotificationPreference>(`${this.url}/preferences`, { category, enabled });
  }
}
