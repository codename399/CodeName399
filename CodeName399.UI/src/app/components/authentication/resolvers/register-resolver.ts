import { inject, Injectable } from "@angular/core";
import { ActivatedRouteSnapshot, Resolve, RouterStateSnapshot } from "@angular/router";
import { UserService } from "../../home/services/user-service";

@Injectable({
    providedIn:"root"
})
export class RegisterResolver implements Resolve<any>{
#userService=inject(UserService);

    resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
        let id = route.paramMap.get("id");
        
        if(id){
            return this.#userService.getById(id);
        }
        else{
            return null;
        }
    }
}