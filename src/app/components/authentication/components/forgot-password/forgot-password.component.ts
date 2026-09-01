import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication-service';
import { ToastService } from '../../../../services/toast.service';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.css'
})
export class ForgotPasswordComponent {
  #fb = inject(FormBuilder);
  #auth = inject(AuthenticationService);
  #router = inject(Router);
  #toast = inject(ToastService);

  step: 'request' | 'reset' = 'request';
  loading = false;
  channel: 'Email' | 'Contact' = 'Email';

  form = this.#fb.nonNullable.group({
    identifier: ['', Validators.required],
    otp: [''],
    newPassword: ['', [Validators.minLength(8)]],
    confirmPassword: ['']
  });

  requestOtp(): void {
    if (this.form.controls.identifier.invalid) return;
    this.loading = true;
    this.#auth.requestForgotPassword(this.form.controls.identifier.value, this.channel).subscribe({
      next: () => { this.step = 'reset'; this.#toast.success('OTP sent.'); },
      error: () => this.#toast.error('Unable to send OTP.'),
      complete: () => this.loading = false
    });
  }

  resetPassword(): void {
    const v = this.form.getRawValue();
    if (!v.otp || !v.newPassword || v.newPassword !== v.confirmPassword) {
      this.#toast.error('Enter a valid OTP and matching passwords.');
      return;
    }
    this.loading = true;
    this.#auth.resetForgottenPassword(v.identifier, v.otp, v.newPassword, this.channel).subscribe({
      next: () => { this.#toast.success('Password reset successfully.'); this.#router.navigate(['/login']); },
      error: () => this.#toast.error('Unable to reset password.'),
      complete: () => this.loading = false
    });
  }

  backToLogin(): void { this.#router.navigate(['/login']); }
}
