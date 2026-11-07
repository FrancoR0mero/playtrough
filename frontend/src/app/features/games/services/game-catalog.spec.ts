import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../environments/environment';
import { GameCatalogService } from './game-catalog';

describe('GameCatalogService', () => {
  let http: HttpTestingController;
  let service: GameCatalogService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(GameCatalogService);
  });

  afterEach(() => http.verify());

  it('searches the configured API with encoded query and pagination parameters', () => {
    const response = { items: [], page: 2, pageSize: 12, hasMore: false };
    const next = vi.fn();
    service.search('Ratchet & Clank', 2, 12).subscribe(next);
    const request = http.expectOne((req) => req.url === `${environment.apiUrl}/games/search`);
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys()).toEqual(['q', 'page', 'pageSize']);
    expect(request.request.params.get('q')).toBe('Ratchet & Clank');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');
    expect(request.request.urlWithParams).toContain('q=Ratchet%20%26%20Clank');
    request.flush(response);
    expect(next).toHaveBeenCalledWith(response);
  });

  it('gets detail by external ID and preserves the local ID', () => {
    const response = { id: 7, externalId: 3498, name: 'Grand Theft Auto V' };
    const next = vi.fn();
    service.getDetail(3498).subscribe(next);
    const request = http.expectOne(`${environment.apiUrl}/games/3498`);
    expect(request.request.method).toBe('GET');
    request.flush(response);
    expect(next).toHaveBeenCalledWith(response);
  });
});
