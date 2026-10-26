import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { GameDetail, GameSearchResponse } from '../models/game';

@Injectable({ providedIn: 'root' })
export class GameCatalogService {
  private readonly http = inject(HttpClient);

  search(q: string, page: number, pageSize: number): Observable<GameSearchResponse> {
    const params = new HttpParams().set('q', q).set('page', page).set('pageSize', pageSize);
    return this.http.get<GameSearchResponse>(`${environment.apiUrl}/games/search`, { params });
  }

  getDetail(externalId: number): Observable<GameDetail> {
    return this.http.get<GameDetail>(`${environment.apiUrl}/games/${externalId}`);
  }
}
