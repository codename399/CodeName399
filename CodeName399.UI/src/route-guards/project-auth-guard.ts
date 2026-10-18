import { inject, Injectable } from "@angular/core";
import { ActivatedRouteSnapshot, CanActivate, GuardResult, MaybeAsync, Router, RouterStateSnapshot } from "@angular/router";
import { AuthenticationService } from "../app/components/authentication/services/authentication-service";
import { Constants } from "../constants";

@Injectable({
    providedIn: "root"
})
export class ProjectAuthGuard implements CanActivate {
    #authService = inject(AuthenticationService);
    #router = inject(Router);

    canActivate(route: ActivatedRouteSnapshot, state: RouterStateSnapshot): MaybeAsync<GuardResult> {
        const isLoggedIn = this.#authService.isLoggedIn();
        const projectName = route.data["projectName"];
        const hasClaim = this.#authService.hasClaim(projectName, "False") || this.#authService.hasClaim(Constants.roleClaim, Constants.admin)

        if (!isLoggedIn && !hasClaim) {
            this.#router.navigate(['/home']);
        }

        return isLoggedIn || hasClaim;
    }
}