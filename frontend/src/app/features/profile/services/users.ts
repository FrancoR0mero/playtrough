import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import {
  MyProfileDto,
  PrivacyDto,
  ProfileDto,
  UpdateProfileRequest,
  UserSearchPage,
} from '../models/user';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/users`;

  getMe() {
    return this.http.get<MyProfileDto>(`${this.url}/me`);
  }

  getPrivacy() {
    return this.http.get<PrivacyDto>(`${this.url}/me/privacy`);
  }

  updatePrivacy(payload: PrivacyDto) {
    return this.http.put<PrivacyDto>(`${this.url}/me/privacy`, payload);
  }

  search(q: string, page = 1, pageSize = 20) {
    return this.http.get<UserSearchPage>(`${this.url}/search`, { params: { q, page, pageSize } });
  }

  getProfile(username: string) {
    return this.http.get<ProfileDto>(`${this.url}/${encodeURIComponent(username)}`);
  }

  updateMe(payload: UpdateProfileRequest) {
    return this.http.put<MyProfileDto>(`${this.url}/me`, payload);
  }
}
