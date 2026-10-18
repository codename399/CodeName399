import {  inject, Injectable } from "@angular/core";
import { CanActivate, Router } from "@angular/router";
import { AuthenticationService } from "../app/components/authentication/services/authentication-service";

@Injectable({
  providedIn: 'root'
})
export class NoAuthGuard implements CanActivate {
  #router: Router = inject(Router);
  #authenticationService: AuthenticationService = inject(AuthenticationService);

  canActivate(): boolean {
    const isLoggedIn = this.#authenticationService.isLoggedIn();
    if (isLoggedIn) {
      this.#router.navigate(['/home']);
    }
    return !isLoggedIn;
  }
}