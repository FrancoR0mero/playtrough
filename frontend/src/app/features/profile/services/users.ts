import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../../../environments/environment';
import { MyProfileDto, ProfileDto, UpdateProfileRequest } from '../models/user';

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/users`;

  getMe() {
    return this.http.get<MyProfileDto>(`${this.url}/me`);
  }

  getProfile(username: string) {
    return this.http.get<ProfileDto>(`${this.url}/${encodeURIComponent(username)}`);
  }

  updateMe(payload: UpdateProfileRequest) {
    return this.http.put<MyProfileDto>(`${this.url}/me`, payload);
  }
}
