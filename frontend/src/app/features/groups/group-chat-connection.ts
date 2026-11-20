import { inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/services/auth';

export interface ChatMessage {
  id: string;
  groupId: number;
  authorId: number;
  username: string;
  text: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class GroupChatConnection {
  private readonly auth = inject(AuthService);

  create(): HubConnection {
    const url = environment.apiUrl.replace(/\/api\/?$/, '') + '/hubs/chat';
    return new HubConnectionBuilder()
      .configureLogging(LogLevel.None)
      .withUrl(url, {
        withCredentials: false,
        accessTokenFactory: () => {
          const access = this.auth.getAccessToken();
          return access && !this.auth.isAccessTokenExpired()
            ? access
            : firstValueFrom(this.auth.refresh({ redirectOnFailure: false }));
        },
      })
      .withAutomaticReconnect()
      .build();
  }
}
