import { inject, Injectable } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  Resolve,
  RouterStateSnapshot,
} from '@angular/router';
import { ProjectService } from '../services/project-service';
import { PaginationRequest } from '../../../models/pagination-request';
import { PAGINATION_REQUEST } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class ProjectResolver implements Resolve<any> {
  #projectService = inject(ProjectService);
  #paginationRequest = inject(PAGINATION_REQUEST);

  resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
    return this.#projectService.getAll(this.#paginationRequest);
  }
}
