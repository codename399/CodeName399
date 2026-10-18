import { Component, effect, inject, ViewChild } from '@angular/core';
import {
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { InputType } from '../../../../models/enums/input-type';
import { ToastService } from '../../../../services/toast.service';
import { GridService } from '../../../authentication/services/grid.service';
import { GridComponent } from '../../../grid/grid.component';
import { InputComponent } from '../../../input/input.component';
import { Role } from '../../models/role';
import { RoleService } from '../../services/role-service';

@Component({
  selector: 'app-role',
  standalone: true,
  imports: [GridComponent, ReactiveFormsModule, InputComponent],
  templateUrl: './role.component.html',
  styleUrl: './role.component.css',
  providers: [GridService],
})
export class RoleComponent {
  #formBuilder = inject(FormBuilder);
  #route = inject(ActivatedRoute);
  #toastService = inject(ToastService);
  #gridService = inject(GridService<Role>);

  form: FormGroup;
  InputType = InputType;

  @ViewChild(GridComponent) gridComponent!: GridComponent<Role>;

  get item() {
    return this.#gridService.item;
  }

  get showForm() {
    return this.#gridService.showForm;
  }

  set item(value: Role | null) {
    this.#gridService.item = value;
  }

  set showForm(value: boolean) {
    this.#gridService.showForm = value;
  }

  constructor() {
    this.#gridService.service = RoleService;
    this.#gridService.pagedResponse =
      this.#route.snapshot.data['pagedResponse'];
    this.#gridService.displayedColumns = ['Name'];

    this.form = this.#formBuilder.group({
      name: ['', [Validators.required]],
    });

    effect(() => {
      if (this.showForm) {
        this.form.patchValue(this.item ?? {});
      }
    });
  }

  onSubmit() {
    let role: Role = this.form.value;

    if (this.form.valid) {
      if (this.item) {
        role.id = this.item.id;
        this.item = role;

        this.#gridService.update();
      } else {
        this.item = role;
        this.#gridService.add();
      }
    } else {
      this.#toastService.error('Invalid form.');
    }
  }
}
