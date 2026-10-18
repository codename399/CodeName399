import { Directive, inject, Input, TemplateRef, ViewContainerRef } from "@angular/core";
import { AuthenticationService } from "../app/components/authentication/services/authentication-service";
import { Constants } from "../constants";

@Directive({
    selector: "[appHasAdminClaim]"
})
export class AdminClaimDirective {
    #templateRef = inject(TemplateRef<any>);
    #viewContainerRef = inject(ViewContainerRef);
    #authService = inject(AuthenticationService);

    private hasView = false;

    constructor() {
        if (this.#authService.hasClaim(Constants.roleClaim, Constants.admin)) {
            if (!this.hasView) {
                this.#viewContainerRef.createEmbeddedView(this.#templateRef);
                this.hasView = true;
            }
        }
        else {
            this.hasView = false;
            this.#viewContainerRef.clear();
        }
    }
}