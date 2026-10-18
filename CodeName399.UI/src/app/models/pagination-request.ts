import { inject } from '@angular/core';
import { Constants } from '../../constants';
import { FilterRequest } from './filter-request';
import { Config } from '../../assets/environments/config';

export class PaginationRequest {
  skip?: number = 0;
  limit?: number = 5;
  sortBy?: string = Constants.updationDate;
  filters?: FilterRequest[];
  ascending?: boolean = false;
  isDeleted?: boolean = false;
  fetchAll?: boolean = false;
}
