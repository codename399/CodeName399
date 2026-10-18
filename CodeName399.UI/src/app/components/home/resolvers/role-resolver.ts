import { inject, Injectable } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  Resolve,
  RouterStateSnapshot,
} from '@angular/router';
import { PAGINATION_REQUEST } from '../../../../injectors/common-injector';
import { RoleService } from '../services/role-service';

@Injectable({
  providedIn: 'root',
})
export class RoleResolver implements Resolve<any> {
  #roleService = inject(RoleService);
  #paginationRequest= inject(PAGINATION_REQUEST);

  resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
    return this.#roleService.getAll(this.#paginationRequest);
  }
}
