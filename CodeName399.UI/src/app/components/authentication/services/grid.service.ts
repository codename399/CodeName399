import { inject, Injectable, Injector, signal } from '@angular/core';
import { PAGINATION_REQUEST } from '../../../../injectors/common-injector';
import { PagedResponse } from '../../../models/paged-response';
import { ToastService } from '../../../services/toast.service';

@Injectable({
  providedIn: 'root',
})
export class GridService<I> {
  #injector = inject(Injector);
  #service!: any;
  #paginationRequest = inject(PAGINATION_REQUEST);
  #toastService = inject(ToastService);
  #pagedResponse = signal<PagedResponse<I> | null>(null);
  #showForm = signal<boolean>(false);
  #displayedColumns = signal<string[]>([]);
  #item!: I | null;

  get service() {
    return this.#service;
  }

  get paginationRequest() {
    return this.#paginationRequest;
  }

  get pagedResponse(): PagedResponse<I> | null {
    return this.#pagedResponse();
  }

  get showForm() {
    return this.#showForm();
  }

  get displayedColumns() {
    return this.#displayedColumns();
  }

  get item() {
    return this.#item;
  }

  set service(value: any) {
    this.#service = this.#injector.get(value);
  }

  set paginationRequest(value: any) {
    this.#paginationRequest.skip = value.pageIndex * value.pageSize;
    this.#paginationRequest.limit = value.pageSize;
  }

  set pagedResponse(value: PagedResponse<I>) {
    this.#pagedResponse.set(value);
  }

  set showForm(value: boolean) {
    this.#showForm.set(value);
  }

  set displayedColumns(value: string[]) {
    this.#displayedColumns.set(value);
  }

  set item(value: I | null) {
    this.#item = value;
  }

  getAll() {
    return this.service.getAll(this.paginationRequest);
  }

  add(refresh: boolean = true) {
    this.service.add(this.item).subscribe({
      next: () => {
        if (refresh) {
          this.showForm = false;
          window.location.reload();
        }

        this.item = null;
        this.#toastService.success('Added successfully');
      },
      error: () => {
        this.item = null;
      }
    });
  }

  update(refresh: boolean = true) {
    this.service.update(this.item).subscribe({
      next: () => {
        if (refresh) {
          this.showForm = false;
          window.location.reload();
        }

        this.item = null;
        this.#toastService.success('Updated successfully');
      },
    });
  }

  delete(event: I[]) {
    return this.service.delete(event.map((m: any) => m.id ?? ''));
  }
}
