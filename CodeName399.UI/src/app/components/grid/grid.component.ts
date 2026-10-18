import { DatePipe } from '@angular/common';
import {
  AfterViewInit,
  Component,
  effect,
  inject,
  input,
  signal,
  viewChild,
  ViewChild
} from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginator, MatPaginatorModule } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { debounceTime, distinctUntilChanged, switchMap } from 'rxjs';
import { Config } from '../../../assets/environments/config';
import { OperatorType } from '../../models/enums/operator-type.enum';
import { PagedResponse } from '../../models/paged-response';
import { CamelCasePipe } from '../../pipes/camelcase-pipe';
import { NoSpacePipe } from '../../pipes/nospace-pipe';
import { ToastService } from '../../services/toast.service';
import { GridService } from '../authentication/services/grid.service';
import { DialogComponent } from '../dialog/dialog.component';

@Component({
  selector: 'app-grid',
  imports: [MatPaginatorModule, NoSpacePipe, CamelCasePipe, DatePipe, MatCheckboxModule, MatTableModule, ReactiveFormsModule, DialogComponent],
  templateUrl: './grid.component.html',
  styleUrl: './grid.component.css'
})
export class GridComponent<I> implements AfterViewInit {
  #gridService = inject(GridService<I>);
  #toastService = inject(ToastService);
  #dialog = inject(MatDialog);
  #config = inject(Config);

  showAdd = input(true);
  showDelete = input(true);
  showSearch = input(true);
  title = input<string>('Name');
  date = input<string>('UpdationDate');

  searchInput: FormControl = new FormControl('');
  loadGridImages: boolean = this.#config.loadGridImages;
  isDeleteMode: boolean = false;
  selection: I[] = [];
  items: Record<string, any>[] = [];

  @ViewChild(MatPaginator) paginator!: MatPaginator;
  @ViewChild(DialogComponent) dialog!: DialogComponent;
  searchBy: FormControl = new FormControl(this.title());
  sortBy: FormControl = new FormControl(this.title());
  sortAscending: boolean = true


  get length() {
    return this.#gridService.pagedResponse?.count;
  }

  get pageSize() {
    return this.#gridService.paginationRequest.limit;
  }

  get currentPage() {
    return this.#gridService.paginationRequest.skip / this.pageSize + 1;
  }

  get showForm() {
    return this.#gridService.showForm;
  }

  get displayedColumns() {
    return this.#gridService.displayedColumns;
  }

  get item() {
    return this.#gridService.item;
  }

  get gridService() {
    return this.#gridService;
  }

  set showForm(value: boolean) {
    this.#gridService.showForm = value;
  }

  set displayedColumns(value: string[]) {
    this.#gridService.displayedColumns = value;
  }

  set item(value: I | null) {
    this.#gridService.item = value;
  }

  constructor() {
    this.searchInput.valueChanges.pipe(
      debounceTime(400),
      distinctUntilChanged(),
      switchMap((term: string) => {
        if (!term || term.length < 0) {
          this.#gridService.paginationRequest.filters = null;
        }
        else {
          this.#gridService.paginationRequest.filters = [
            {
              key: this.sortBy.value,
              value: term,
              operator: OperatorType.Like
            }
          ]
        }

        return this.#gridService.getAll();
      })
    ).subscribe((result => {
      this.#gridService.pagedResponse = result as PagedResponse<I>;
      this.items = this.#gridService.pagedResponse?.items ?? [];
    }))

    effect(() => {
      this.items = this.#gridService.pagedResponse?.items ?? [];
    });
  }

  ngAfterViewInit() {
    if (this.paginator) {
      this.paginator.page.subscribe((event: any) => {
        this.#gridService.paginationRequest = event;
        this.#gridService.paginationRequest.limit = this.#config.pageSize;
        this.getAll();
      });
    }
  }

  onSort() {
    this.#gridService.paginationRequest = {
      pageIndex: 0,
      pageSize: this.pageSize
    }
    this.#gridService.paginationRequest.sortBy = this.sortBy.value;
    this.#gridService.paginationRequest.ascending = this.sortAscending;

    this.getAll();
  }

  getAll() {
    this.#gridService.getAll().subscribe({
      next: (pagedResponse: PagedResponse<I>) => {
        this.#gridService.pagedResponse = pagedResponse;

        this.selection = [];
      },
    });
  }

  add() {
    this.showForm = true;
  }

  edit(item: I) {
    this.item = item;
    this.showForm = true;
  }

  openDeleteConfirmation(item?: I) {
    this.dialog.openDialog();
  }

  delete(item?: I) {
    let items: I[] = item ? [item] : this.selection;

    this.#gridService.delete(items).subscribe({
      next: () => {
        this.getAll();
        this.#toastService.success('Deleted successfully');
      },
    });
  }

  isValidDate(dateString: string): boolean {
    if (typeof dateString !== "string") return false;
    const date = new Date(dateString);
    return !isNaN(date.getTime())
  }

  changeSortDirection() {
    this.sortAscending = !this.sortAscending;
    this.onSort();
  }

  enableDeleteMode() {
    this.isDeleteMode = true;
  }

  disableDeleteMode() {
    this.isDeleteMode = false;
    this.selection = [];
  }

  isSelected(item: any) {
    if (this.selection.includes(item) && this.isDeleteMode) {
      return true;
    }

    return false;
  }

  onTileClick(item: any) {
    if (this.isDeleteMode) {
      this.addSelection(item);
    }
    else {
      this.edit(item);
    }
  }

  addSelection(item: I) {
    let index = this.selection.indexOf(item);

    if (index > -1) {
      this.selection.splice(index, 1);
    }
    else {
      this.selection.push(item);
    }
  }

  getTileIndex(index: number) {
    return (index + 1) + ((this.currentPage - 1) * this.pageSize);
  }
}
