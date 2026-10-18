import { HttpClient } from "@angular/common/http";
import { inject, Injectable } from "@angular/core";
import { Observable } from "rxjs";
import { ApiConstants } from "../../../../api-constants";
import { PagedResponse } from "../../../models/paged-response";
import { PaginationRequest } from "../../../models/pagination-request";
import { UserProjectMappingDto } from "../models/dto/user-project-mapping-dto";
import { API_CONSTANTS } from "../../../../injectors/common-injector";

@Injectable({
    providedIn: "root"
})
export class UserProjectMappingService {
    #httpClient = inject(HttpClient);
    #apiConstants = inject(API_CONSTANTS);

    getAll(
        paginationRequest: PaginationRequest
    ): Observable<PagedResponse<UserProjectMappingDto>> {
        return this.#httpClient.post<PagedResponse<UserProjectMappingDto>>(
            this.#apiConstants.getUrl(this.#apiConstants.getAllUserProjectMappings, true),
            paginationRequest
        );
    }

    update(
        userProjectMapping: UserProjectMappingDto
    ) {
        return this.#httpClient.put(
            this.#apiConstants.getUrl(this.#apiConstants.updateUserProjectMappings, true),
            userProjectMapping
        );
    }
}