import { inject, Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../features/auth/services/auth';

@Injectable({ providedIn: 'root' })
export class RealtimeConnection {
  private readonly auth = inject(AuthService);

  create(path: `/hubs/${string}`): HubConnection {
    const url = environment.apiUrl.replace(/\/api\/?$/, '') + path;
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
