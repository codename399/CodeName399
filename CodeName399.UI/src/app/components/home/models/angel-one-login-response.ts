export interface AngelOneLoginResponse {
  status: string;
  messge: string;
  data: AngelOneLoginData;
}

export interface AngelOneLoginData {
  jwtToken: string;
  refreshToken: string;
  feedToken: string;
}
