import { Component, inject } from "@angular/core";
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from "@angular/forms";
import { Router } from "@angular/router";
import { ToastService } from "../../../../services/toast.service";
import { AuthenticationService } from "../../services/authentication-service";
import { InputComponent } from "../../../input/input.component";
import { InputType } from "../../../../models/enums/input-type";

@Component({
  selector: 'app-login-component',
  templateUrl: './login-component.html',
  styleUrl: './login-component.css',
  imports: [ReactiveFormsModule, InputComponent],
})
export class LoginComponent {
  #authenticationService = inject(AuthenticationService);
  #formBuilder = inject(FormBuilder);
  #router = inject(Router);
  #toastService = inject(ToastService);
  form: FormGroup;
  rememberMe = [{ id: "Remember Me", name: "Remember Me" }];
  InputType = InputType;

  constructor() {
    // Initialization code can go here
    this.form = this.#formBuilder.group({
      username: ['', Validators.required],
      password: ['', Validators.required],
    });
  }

  // Example method to handle user login
  onLogin() {
    if (this.form.invalid) {
      return;
    }

    this.validateUser(this.form.value);
  }

  // Method to validate user credentials
  validateUser(loginRequest: any) {

    this.#authenticationService.validateUser(loginRequest).subscribe({
      next: (response) => {
        this.#router.navigate(['/home']);
        this.#toastService.success('Login successful!');
      }
    });
  }

  gotoRegister() {
    this.#router.navigate(['/register']);
  }
}
