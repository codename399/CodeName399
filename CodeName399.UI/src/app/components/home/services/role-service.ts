import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Role } from '../models/role';
import { Observable } from 'rxjs';
import { ApiConstants } from '../../../../api-constants';
import { PaginationRequest } from '../../../models/pagination-request';
import { PagedResponse } from '../../../models/paged-response';
import { API_CONSTANTS } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class RoleService {
  #httpClient = inject(HttpClient);
  #apiConstants = inject(API_CONSTANTS);

  getAll(request: PaginationRequest): Observable<PagedResponse<Role>> {
    return this.#httpClient.post<PagedResponse<Role>>(
      this.#apiConstants.getUrl(this.#apiConstants.getAllRoles, true),
      request
    );
  }

  add(role: Role) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.addRoles, true),
      role
    );
  }

  update(role: Role) {
    return this.#httpClient.put(
      this.#apiConstants.getUrl(this.#apiConstants.updateRole, true),
      role
    );
  }

  delete(roleIds: string[]) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.deleteRole, true),
      roleIds
    );
  }
}
