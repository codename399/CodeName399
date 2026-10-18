import {
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpStatusCode
} from '@angular/common/http';

import { inject } from '@angular/core';

import {
  catchError,
  switchMap,
  throwError
} from 'rxjs';
import { AuthenticationService } from '../app/components/authentication/services/authentication-service';

export const refreshTokenInterceptor: HttpInterceptorFn =
(req, next) => {
  const authService = inject(AuthenticationService);

  return next(req).pipe(

    catchError((error: HttpErrorResponse) => {

      if (error.status !== HttpStatusCode.Unauthorized) {
        return throwError(() => error);
      }

      const refreshToken =
        authService.refreshToken;

      if (!refreshToken) {
        authService.clearToken();
        return throwError(() => error);
      }

      return authService.refresh().pipe(
        switchMap(response => {

          authService.token = response.token;
          authService.refreshToken = response.refreshToken;

          const clonedRequest = req.clone({
            setHeaders: {
              Authorization:
                `Bearer ${response.token}`
            }
          });

          return next(clonedRequest);
        }),

        catchError(refreshError => {

          authService.clearToken();

          window.location.href = '/login';

          return throwError(() => refreshError);
        })
      );
    })
  );
};