export interface ConfirmEmailRequest {
  email: string;
  token: string;
}

export interface ResendConfirmationRequest {
  email: string;
}
