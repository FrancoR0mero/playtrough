import type { LoginResponse } from './login';

export interface RefreshRequest {
  refreshToken: string;
}

export type RefreshResponse = LoginResponse;
