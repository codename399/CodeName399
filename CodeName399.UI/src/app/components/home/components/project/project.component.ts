import { Component, effect, inject, ViewChild } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ActivatedRoute } from '@angular/router';
import { Config } from '../../../../../assets/environments/config';
import { FileUploadService } from '../../../../services/file-upload.service';
import { ToastService } from '../../../../services/toast.service';
import { GridService } from '../../../authentication/services/grid.service';
import { GridComponent } from '../../../grid/grid.component';
import { Project } from '../../models/project';
import { User } from '../../models/user';
import { ProjectService } from '../../services/project-service';
import { InputComponent } from '../../../input/input.component';
import { InputType } from '../../../../models/enums/input-type';

@Component({
  selector: 'app-project',
  standalone: true,
  imports: [GridComponent, MatCheckboxModule, ReactiveFormsModule, InputComponent],
  templateUrl: './project.component.html',
  styleUrl: './project.component.css',
  providers: [FileUploadService]
})
export class ProjectComponent {
  #route = inject(ActivatedRoute);
  #formBuilder = inject(FormBuilder);
  #toastService = inject(ToastService);
  #gridService = inject(GridService<User>);
  #fileUploadService = inject(FileUploadService);
  #config = inject(Config);

  InputType = InputType;
  form: FormGroup;
  isAdmin = [{ id: "Is Admin", name: "Is Admin" }];

  @ViewChild(GridComponent) gridComponent!: GridComponent<Project>;

  get item() {
    return this.#gridService.item;
  }

  get showForm() {
    return this.#gridService.showForm;
  }

  get url() {
    return this.#fileUploadService.url;
  }

  set url(value: string) {
    this.#fileUploadService.url = value;
  }

  set item(value: Project | null) {
    this.#gridService.item = value;
  }

  set showForm(value: boolean) {
    this.#gridService.showForm = value;
  }

  constructor() {
    this.#gridService.service = ProjectService;
    this.#gridService.pagedResponse =
      this.#route.snapshot.data['pagedResponse'];
    this.#gridService.displayedColumns = ['Name', 'Description'];

    this.form = this.#formBuilder.group({
      name: ['', [Validators.required]],
      description: [''],
      imageUrl: [this.url],
      route: [''],
      navigationText: [''],
      isAdmin: [false]
    });

    effect(() => {
      if (this.showForm) {
        if (this.item) {
          this.url =
            this.item?.imageUrl ?? this.#config.profilePictureUrl;
        }

        this.form.patchValue(this.item ?? {});
      }
    });
  }

  onSubmit() {
    this.form.patchValue({ imageUrl: this.url });
    let project: Project = this.form.value;

    if (this.form.valid) {
      if (this.item) {
        project.id = this.item.id;
        this.item = project;

        this.#gridService.update();
      } else {
        this.item = project;
        this.#gridService.add();
      }
    } else {
      this.#toastService.error('Invalid form.');
    }
  }

  onFileSelected(event: any) {
    this.#fileUploadService.onFileSelected(event);
  }
}
