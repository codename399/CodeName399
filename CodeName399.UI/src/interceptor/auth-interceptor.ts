import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { jwtDecode } from 'jwt-decode';
import { catchError, finalize, throwError } from 'rxjs';
import { AuthenticationService } from '../app/components/authentication/services/authentication-service';
import { LoaderService } from '../app/services/loader.service';
import { ToastService } from '../app/services/toast.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authenticationService = inject(AuthenticationService);
  const loaderService = inject(LoaderService);
  const toastService = inject(ToastService);
  const token: string = authenticationService.token ?? '';

  loaderService.show();

  if (token) {
    req = req.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      let message: string = error.error?.Message ?? error.message;

      loaderService.hide();
      toastService.error(message);

      return throwError(() => error);
    }),
    finalize(() => loaderService.hide())
  );
};
