import { Component, effect, inject, input, OnInit, output } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Config } from '../../../../../../assets/environments/config';
import { PasswordMatchValidator } from '../../../../../../validators/password-match-validator';
import { FileUploadService } from '../../../../../services/file-upload.service';
import { ToastService } from '../../../../../services/toast.service';
import { User } from '../../../../home/models/user';
import { InputComponent } from '../../../../input/input.component';
import { InputType } from '../../../../../models/enums/input-type';

@Component({
  selector: 'app-registration-form',
  imports: [ReactiveFormsModule, InputComponent],
  templateUrl: './registration-form.component.html',
  styleUrl: './registration-form.component.css',
  providers: [FileUploadService]
})
export class RegistrationFormComponent implements OnInit {
  #formBuilder = inject(FormBuilder);
  #toastService = inject(ToastService);
  #fileUploadService = inject(FileUploadService);
  #config = inject(Config);

  user = input<User | null>();
  back = input<Function>();
  onFormSubmit = output<User>();

  InputType = InputType;
  form: FormGroup;
  id!: string | null;
  roleId!: string | null;

  get profilePictureUrl() {
    return this.#fileUploadService.url;
  }

  set profilePictureUrl(value: string) {
    this.#fileUploadService.url = value;
  }

  constructor() {
    this.form = this.#formBuilder.group(
      {
        name: ['', Validators.required],
        username: ['', Validators.required],
        password: [
          '',
          [
            Validators.required,
            Validators.pattern(
              /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!@#$%^&*(),.?":{}|<>]).{8,}$/
            ),
          ],
        ], // At least 8 characters, 1 uppercase, 1 lowercase, 1 number
        confirmPassword: ['', [Validators.required]],
        emailId: ['', [Validators.required, Validators.email]],
        contactNumber: [
          '',
          [Validators.required, Validators.pattern('^[0-9]{10}$')],
        ],
        profilePicture: [this.#config.profilePictureUrl],
      },
      {
        validators: PasswordMatchValidator,
      }
    );

    effect(() => {
      this.form.patchValue({ profilePicture: this.profilePictureUrl }); // set Base64 string
    });
  }

  ngOnInit(): void {
    if (this.user()) {
      this.form.patchValue(this.user() ?? {});
      this.id = this.user()?.id ?? null;
      this.roleId = this.user()?.roleId ?? null;

      if (this.user()?.profilePicture) {
        this.profilePictureUrl =
          this.user()?.profilePicture ?? this.#config.profilePictureUrl;
      }

      if (this.user()) {
        this.form.controls['username'].clearValidators();
        this.form.controls['password'].clearValidators();
        this.form.controls['confirmPassword'].clearValidators();
      }
    }
  }

  onSubmit(): void {
    if (this.form.valid) {
      this.onFormSubmit.emit({
        id: this.id,
        roleId: this.roleId,
        ...this.form.value,
      });
    } else {
      this.#toastService.error('Form is invalid');
    }
  }

  onFileSelected(event: any) {
    this.#fileUploadService.onFileSelected(event);
  }
}
