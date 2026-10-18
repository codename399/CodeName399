import { Component, OnInit, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '../../../../services/toast.service';
import { User } from '../../../home/models/user';
import { UserService } from '../../../home/services/user-service';
import { RegistrationFormComponent } from './registration-form/registration-form.component';

@Component({
  selector: 'app-register',
  imports: [RegistrationFormComponent],
  templateUrl: './register.component.html',
  styleUrl: './register.component.css',
})
export class RegisterComponent implements OnInit {
  #userService = inject(UserService);
  #router = inject(Router);
  #toastService = inject(ToastService);
  #route = inject(ActivatedRoute);
  user!: User;
  users: User[] = [];

  ngOnInit() {
    this.users = this.#route.snapshot.data['users'];

    if (!!this.users?.length) {
      this.user = this.users[0];
    }
  }

  onSubmit(event: User) {
    if (!this.user) {
      this.add(event);
    } else {
      this.update(event);
    }
  }

  add(user: User) {
    if (user) {
      this.#userService.add(user).subscribe({
        next: (response) => {
          this.#toastService.success('Registration successful!');
          this.#router.navigate(['/login']);
        }
      });
    } else {
      this.#toastService.error('Form is invalid');
    }
  }

  update(user: User) {
    if (user) {
      this.#userService.update(user).subscribe({
        next: (response) => {
          this.#toastService.success('Updation successful!');
          this.#router.navigate(['/home']);
        }
      });
    } else {
      this.#toastService.error('Form is invalid');
    }
  }
}
