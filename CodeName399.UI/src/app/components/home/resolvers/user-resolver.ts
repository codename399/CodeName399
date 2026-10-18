import { inject, Injectable } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  Resolve,
  RouterStateSnapshot,
} from '@angular/router';
import { UserService } from '../services/user-service';
import { PAGINATION_REQUEST } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class UserResolver implements Resolve<any> {
  #userService = inject(UserService);
  #paginationRequest = inject(PAGINATION_REQUEST);

  resolve(route: ActivatedRouteSnapshot, state: RouterStateSnapshot) {
    return this.#userService.getAll(this.#paginationRequest);
  }
}
