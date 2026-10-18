import { InjectionToken, Injector } from '@angular/core';
import { PaginationRequest } from '../app/models/pagination-request';
import { ApiConstants } from '../api-constants';

export const PAGINATION_REQUEST = new InjectionToken<PaginationRequest>('pagination-request');
export const CONFIG_FILE_PATH = new InjectionToken<string>('string');
export const API_CONSTANTS = new InjectionToken<ApiConstants>('api-constants');
