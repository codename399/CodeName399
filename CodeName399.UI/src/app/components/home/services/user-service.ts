import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { User } from '../models/user';
import { map, Observable } from 'rxjs';
import { ChangePasswordRequest } from '../../authentication/models/change-password-request';
import { ApiConstants } from '../../../../api-constants';
import { PagedResponse } from '../../../models/paged-response';
import { PaginationRequest } from '../../../models/pagination-request';
import { API_CONSTANTS } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class UserService {
  #httpClient = inject(HttpClient);
  #apiConstants = inject(API_CONSTANTS);

  getAll(
    paginationRequest: PaginationRequest
  ): Observable<PagedResponse<User>> {
    return this.#httpClient.post<PagedResponse<User>>(
      this.#apiConstants.getUrl(this.#apiConstants.getAllUsers, true),
      paginationRequest
    );
  }

  getById(userId: string): Observable<User[]> {
    return this.#httpClient.get<User[]>(
      this.#apiConstants.getUrl(this.#apiConstants.getUserById, true) + '?id=' + userId
    );
  }

  getByProject(projectName: string): Observable<User[]> {
    return this.#httpClient.get<User[]>(
      this.#apiConstants.getUrl(this.#apiConstants.getByProject, true) + '?projectName=' + projectName
    );
  }

  add(user: User) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.addUsers, true),
      user
    );
  }

  update(user: User) {
    return this.#httpClient.put(
      this.#apiConstants.getUrl(this.#apiConstants.updateUser, true),
      user
    );
  }

  delete(userIds: string[]) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.deleteUser, true),
      userIds
    );
  }

  changePassword(changePasswordRequest: ChangePasswordRequest) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.changePassword, true),
      changePasswordRequest
    );
  }
}
