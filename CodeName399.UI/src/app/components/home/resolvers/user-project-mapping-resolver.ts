import { inject, Injectable } from "@angular/core";
import { ActivatedRouteSnapshot, Resolve, RouterStateSnapshot } from "@angular/router";
import { combineLatest, forkJoin, merge } from "rxjs";
import { PAGINATION_REQUEST } from "../../../../injectors/common-injector";
import { ProjectService } from "../services/project-service";
import { UserService } from "../services/user-service";
import { Constants } from "../../../../constants";
import { OperatorType } from "../../../models/enums/operator-type.enum";

@Injectable({
    providedIn: "root"
})
export class UserProjectMappingResolver implements Resolve<any> {
    #userService = inject(UserService);
    #projectService = inject(ProjectService);
    #paginationRequest = inject(PAGINATION_REQUEST);

    resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
        let userPaginationRequest = JSON.parse(JSON.stringify(this.#paginationRequest));
        let projectPaginationRequest = JSON.parse(JSON.stringify(this.#paginationRequest));
        
        userPaginationRequest.fetchAll = true;
        projectPaginationRequest.fetchAll = true;
        projectPaginationRequest.filters = [
            {
                key: Constants.isAdmin,
                value: "false",
                operator: OperatorType.Equal
            }
        ]

        return forkJoin(this.#userService.getAll(userPaginationRequest), this.#projectService.getAll(projectPaginationRequest));
    }
}