import { inject, Injectable } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  Resolve,
  RouterStateSnapshot,
} from '@angular/router';
import { map } from 'rxjs';
import { PAGINATION_REQUEST } from '../../../../injectors/common-injector';
import { ProjectService } from '../services/project-service';
import { AuthenticationService } from '../../authentication/services/authentication-service';

@Injectable({
  providedIn: 'root',
})
export class DashboardResolver implements Resolve<any> {
  #projectService = inject(ProjectService);
  #authService = inject(AuthenticationService);

  resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
    return this.#projectService
      .getAllMapped(this.#authService.userId ?? "");
  }
}
