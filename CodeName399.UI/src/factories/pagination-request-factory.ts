import { inject } from '@angular/core';
import { PaginationRequest } from '../app/models/pagination-request';
import { Config } from '../assets/environments/config';

export function paginationRequestFactory() {
  let config = inject(Config);

  let paginationRequest: PaginationRequest = new PaginationRequest();
  paginationRequest.limit = config.pageSize;

  return paginationRequest;
}
