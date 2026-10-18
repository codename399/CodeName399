import { Component, inject } from '@angular/core';
import { FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Constants } from '../../../../../constants';
import { PAGINATION_REQUEST } from '../../../../../injectors/common-injector';
import { InputType } from '../../../../models/enums/input-type';
import { OperatorType } from '../../../../models/enums/operator-type.enum';
import { PagedResponse } from '../../../../models/paged-response';
import { ToastService } from '../../../../services/toast.service';
import { InputComponent } from '../../../input/input.component';
import { UserProjectMappingDto } from '../../models/dto/user-project-mapping-dto';
import { Project } from '../../models/project';
import { User } from '../../models/user';
import { UserProjectMappingService } from '../../services/user-project-mapping-service';

@Component({
  selector: 'app-user-project-mapping',
  imports: [ReactiveFormsModule, InputComponent],
  templateUrl: './user-project-mapping.component.html',
  styleUrl: './user-project-mapping.component.css'
})
export class UserProjectMappingComponent {
  #route = inject(ActivatedRoute);
  #paginationRequest = inject(PAGINATION_REQUEST);
  #userProjectMappingService = inject(UserProjectMappingService);
  #formBuilder = inject(FormBuilder);
  #toastService = inject(ToastService);

  users: User[] = [];
  projects: Project[] = [];
  userProjectMapping!: UserProjectMappingDto | null;
  form!: FormGroup;
  InputType = InputType;

  get userId() {
    return this.form.get("userId") as FormControl;
  }

  get projectIds() {
    return this.form.get("projectIds") as FormControl;
  }

  constructor() {
    this.users = this.#route.snapshot.data["usersandprojects"][0]?.items;
    this.projects = this.#route.snapshot.data["usersandprojects"][1]?.items;

    this.form = this.#formBuilder.group({
      userId: [null, [Validators.required]],
      projectIds: [[]]
    });

    this.userId.valueChanges.subscribe((userId: string) => {
      if (userId) {
        this.projectIds.reset();
        this.getAll(userId);
      }
    })
  }

  getAll(userId: string) {
    this.#paginationRequest.filters = [
      {
        key: Constants.userId,
        value: userId,
        operator: OperatorType.Equal
      }
    ]

    this.#userProjectMappingService.getAll(this.#paginationRequest).subscribe((pagedResponse: PagedResponse<UserProjectMappingDto>) => {
      if (pagedResponse) {
        if (pagedResponse.count > 0) {
          this.userProjectMapping = pagedResponse.items[0];
          this.projectIds.setValue(this.userProjectMapping.projectIds);
        }
      }
    });
  }

  onSelectionChange(event: any, project: Project) {
    let selections: string[] = this.projectIds.value ?? [];

    if (event.target.checked) {
      if (!selections.includes(project.id ?? "")) {
        this.projectIds.setValue([...selections, project.id]);
      }
    }
    else {
      selections = selections.filter((f: any) => f != project.id);
      this.projectIds.setValue(selections);
    }
  }

  onSubmit() {
    if (this.form.valid) {
      let userProjectMapping: UserProjectMappingDto = this.form.value;

      if (this.userProjectMapping != null) {
        userProjectMapping.id = this.userProjectMapping.id;
      }

      this.#userProjectMappingService.update(userProjectMapping).subscribe(() => {
        this.#toastService.success("Projects mapped successfully");
        this.form.reset();
      });
    }
    else {
      this.#toastService.error("Please select projects to map");
    }
  }

  setSelection(projectId: string) {
    let selections: string[] = this.projectIds.value ?? [];

    if (selections.includes(projectId)) {
      return true;
    }

    return false;
  }
}
