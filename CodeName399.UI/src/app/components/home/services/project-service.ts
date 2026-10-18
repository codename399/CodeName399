import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Project } from '../models/project';
import { ApiConstants } from '../../../../api-constants';
import { PaginationRequest } from '../../../models/pagination-request';
import { PagedResponse } from '../../../models/paged-response';
import { API_CONSTANTS } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class ProjectService {
  #httpClient = inject(HttpClient);
  #apiConstants = inject(API_CONSTANTS);

  getAll(request: PaginationRequest): Observable<PagedResponse<Project>> {
    return this.#httpClient.post<PagedResponse<Project>>(
      this.#apiConstants.getUrl(this.#apiConstants.getAllProjects, true),
      request
    );
  }

  getAllMapped(userId: string): Observable<Project[]> {
    return this.#httpClient.get<Project[]>(
      `${this.#apiConstants.getUrl(this.#apiConstants.getAllMappedProjects, true)}?userId=${userId}`
    );
  }

  getById(projectId: string): Observable<Project[]> {
    return this.#httpClient.get<Project[]>(
      this.#apiConstants.getUrl(this.#apiConstants.getProjectById, true) +
      '?id=' +
      projectId
    );
  }

  add(project: Project) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.addProjects, true),
      project
    );
  }

  update(project: Project) {
    return this.#httpClient.put(
      this.#apiConstants.getUrl(this.#apiConstants.updateProject, true),
      project
    );
  }

  delete(projectIds: string[]) {
    return this.#httpClient.post(
      this.#apiConstants.getUrl(this.#apiConstants.deleteProject, true),
      projectIds
    );
  }
}
