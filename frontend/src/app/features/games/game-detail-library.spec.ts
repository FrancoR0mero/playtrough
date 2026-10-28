import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../environments/environment';
import { jwtInterceptor } from '../../core/interceptors/jwt-interceptor';
import { createSession } from '../auth/auth-test-helpers';
import { TokenStorageService } from '../auth/services/token-storage';
import { LibraryService } from '../library/services/library';
import { GameDetailComponent } from './game-detail';

describe('Game detail and library integration', () => {
  let fixture: ComponentFixture<GameDetailComponent>;
  let http: HttpTestingController;
  let storage: TokenStorageService;
  const url = `${environment.apiUrl}/library`;
  const game = {
    externalId: 4200,
    name: 'Portal 2',
    slug: 'portal-2',
    coverUrl: null,
    releaseYear: 2011,
    platforms: [],
  };
  const entry = {
    gameId: 17,
    ...game,
    status: 'playing',
    statusChangedOn: '2026-10-27',
    addedAt: '2026-10-28T02:30:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [GameDetailComponent],
      providers: [
        provideHttpClient(withInterceptors([jwtInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: DIALOG_DATA, useValue: game },
        { provide: DialogRef, useValue: { close: vi.fn() } },
      ],
    });
    storage = TestBed.inject(TokenStorageService);
    storage.save(createSession());
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(GameDetailComponent);
    fixture.detectChanges();
    http.expectNone((req) => req.url.startsWith(url));
    http.expectOne(`${environment.apiUrl}/games/4200`).flush({
      ...game,
      id: 17,
      coverUrlLarge: null,
      releaseDate: null,
      summary: '',
      genres: [],
      developers: [],
    });
    fixture.detectChanges();
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      fixture.destroy();
      storage.clear();
      TestBed.resetTestingModule();
      vi.useRealTimers();
    }
  });
  function button(action: string): HTMLButtonElement {
    return fixture.nativeElement.querySelector(`[data-action="${action}"]`);
  }

  it('uses the loaded local ID, treats absence normally, and emits the added entry', () => {
    const lookup = http.expectOne(`${url}/games/17`);
    expect(lookup.request.headers.get('Authorization')).toBe('Bearer access');
    lookup.flush(
      { extensions: { code: 'not_in_library' } },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('no está en tu biblioteca');
    const changed = vi.fn();
    TestBed.inject(LibraryService).changes$.subscribe(changed);
    button('choose-playing').click();
    fixture.detectChanges();
    button('save-library').click();
    const request = http.expectOne(url);
    expect(request.request.body).toEqual({ gameId: 17, status: 'playing' });
    request.flush(entry, { status: 201, statusText: 'Created' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-library-status]')?.textContent).toBe(
      'Jugando',
    );
    expect(fixture.nativeElement.querySelector('time')?.textContent).toBe('27/10/2026');
    expect(changed).toHaveBeenCalledExactlyOnceWith({ type: 'added', gameId: 17, entry });
  });

  it('uses the interceptor Retry-After countdown for lookup failures', () => {
    vi.useFakeTimers();
    http.expectOne(`${url}/games/17`).flush(
      { extensions: { code: 'rate_limited' } },
      {
        status: 429,
        statusText: 'Too Many Requests',
        headers: { 'Retry-After': '2' },
      },
    );
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('app-library-entry [role="alert"]')?.textContent,
    ).toContain('2 segundos');
    expect(button('retry-library').disabled).toBe(true);
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    expect(button('retry-library').textContent).toContain('1 s');
    vi.advanceTimersByTime(1000);
    fixture.detectChanges();
    http.expectNone(`${url}/games/17`);
    expect(button('retry-library').disabled).toBe(false);
    button('retry-library').click();
    http.expectOne(`${url}/games/17`).flush(entry);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
    expect(storage.readTokens()?.accessToken).toBe('access');
  });

  it('cancels the library request when the owning detail modal is destroyed', () => {
    const lookup = http.expectOne(`${url}/games/17`);
    fixture.destroy();
    expect(lookup.cancelled).toBe(true);
  });
});
