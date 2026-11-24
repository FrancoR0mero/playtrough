import { TestBed } from '@angular/core/testing';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { of } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../features/auth/services/auth';
import { RealtimeConnection } from './realtime-connection';

describe('RealtimeConnection', () => {
  it('uses the configured hub, reconnects automatically and refreshes the token when it expires', async () => {
    const auth = {
      getAccessToken: vi.fn().mockReturnValue('first-token'),
      isAccessTokenExpired: vi.fn().mockReturnValue(false),
      refresh: vi.fn().mockReturnValue(of('renewed-token')),
    };
    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: auth }],
    });
    const logging = vi.spyOn(HubConnectionBuilder.prototype, 'configureLogging');
    const transport = vi.spyOn(HubConnectionBuilder.prototype, 'withUrl');
    const reconnect = vi.spyOn(HubConnectionBuilder.prototype, 'withAutomaticReconnect');
    try {
      const connection = TestBed.inject(RealtimeConnection).create('/hubs/notifications');
      const [url, options] = transport.mock.calls[0];
      expect(url).toBe(environment.apiUrl.replace(/\/api\/?$/, '') + '/hubs/notifications');
      expect(connection.baseUrl).toBe(url);
      expect(logging).toHaveBeenCalledWith(LogLevel.None);
      expect(reconnect).toHaveBeenCalledOnce();
      expect(options).toMatchObject({ withCredentials: false });
      const token = options.accessTokenFactory!;
      expect(await token()).toBe('first-token');
      expect(auth.refresh).not.toHaveBeenCalled();
      auth.isAccessTokenExpired.mockReturnValue(true);
      expect(await token()).toBe('renewed-token');
      expect(auth.refresh).toHaveBeenCalledWith({ redirectOnFailure: false });
    } finally {
      logging.mockRestore();
      transport.mockRestore();
      reconnect.mockRestore();
    }
  });
});
