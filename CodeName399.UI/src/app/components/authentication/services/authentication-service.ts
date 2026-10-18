import { inject, Injectable, signal } from '@angular/core';
import { LoginRequest } from '../models/login-request';
import { HttpClient } from '@angular/common/http';
import { tap } from 'rxjs';
import { User } from '../../home/models/user';
import { ApiConstants } from '../../../../api-constants';
import { Constants } from '../../../../constants';
import { LoginResponse } from '../models/login-response';
import { Router } from '@angular/router';
import { API_CONSTANTS } from '../../../../injectors/common-injector';
import { jwtDecode } from 'jwt-decode';
import { RefreshTokenRequest } from '../models/refresh-token-request';

@Injectable({
  providedIn: 'root',
})
export class AuthenticationService {
  #httpClient = inject(HttpClient);
  #user = signal<User | null>(null);
  #route = inject(Router);
  #apiConstants = inject(API_CONSTANTS);

  get token() {
    return localStorage.getItem(Constants.token);
  }

  set token(value: string | null) {
    localStorage.setItem(Constants.token, value ?? '');
  }

  get refreshToken() {
    return localStorage.getItem(Constants.refreshToken);
  }

  set refreshToken(value: string | null) {
    localStorage.setItem(Constants.refreshToken, value ?? '');
  }

  get userId() {
    return localStorage.getItem(Constants.userId);
  }

  set userId(value: string | null) {
    localStorage.setItem(Constants.userId, value ?? '');
  }

  get user() {
    return this.#user();
  }

  set user(value: User | null) {
    this.#user.set(value);
  }

  clearToken() {
    localStorage.clear();
    this.#user.set(null);
  }

  isLoggedIn(): boolean {
    return this.token !== '' && this.token !== null;
  }

  logout() {
    this.clearToken();
    this.#route.navigate(['/login']);
  }

  validateUser(loginRequest: LoginRequest) {
    return this.#httpClient
      .post<LoginResponse>(
        this.#apiConstants.getUrl(this.#apiConstants.validateUser, true),
        loginRequest
      )
      .pipe(
        tap((response) => {
          if (response?.token) {
            this.userId = response.userId;
            this.token = response.token;
            this.refreshToken = response.refreshToken;
          }
        })
      );
  }

  refresh() {
    const refreshTokenRequest: RefreshTokenRequest = {
      refreshToken: this.refreshToken ?? ''
    }

    return this.#httpClient
      .post<LoginResponse>(
        this.#apiConstants.getUrl(this.#apiConstants.refresh, true),
        refreshTokenRequest
      )
      .pipe(
        tap((response) => {
          if (response?.token) {
            this.userId = response.userId;
            this.token = response.token;
            this.refreshToken = response.refreshToken;
          }
        })
      );
  }

  getClaims() {
    if (this.token) {
      const decodedToken = jwtDecode(this.token);
      return decodedToken;
    }

    return null;
  }

  hasClaim(claimName: string, value?: string) {
    const claims: any = this.getClaims();

    if (!claims) {
      return false;
    }

    if (value) {
      return claims[claimName] && claims[claimName] == value;
    }

    return !!claims[claimName];
  }
}
