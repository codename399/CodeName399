import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication-service';
import { ToastService } from '../../../../services/toast.service';

@Component({
  selector: 'app-otp-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './otp-login.component.html',
  styleUrl: './otp-login.component.css'
})
export class OtpLoginComponent {
  #fb = inject(FormBuilder);
  #auth = inject(AuthenticationService);
  #router = inject(Router);
  #toast = inject(ToastService);

  channel: 'Email' | 'Contact' = 'Email';
  requested = false;
  loading = false;

  form = this.#fb.nonNullable.group({
    identifier: ['', Validators.required],
    otp: ['']
  });

  requestOtp(): void {
    if (this.form.controls.identifier.invalid) return;
    this.loading = true;
    this.#auth.requestOtp(this.form.controls.identifier.value, this.channel).subscribe({
      next: () => { this.requested = true; this.#toast.success('OTP sent.'); },
      error: () => this.#toast.error('Unable to send OTP.'),
      complete: () => this.loading = false
    });
  }

  verify(): void {
    const v = this.form.getRawValue();
    if (!v.identifier || !v.otp) return;
    this.loading = true;
    this.#auth.verifyOtp(v.identifier, v.otp, this.channel).subscribe({
      next: () => { this.#toast.success('Login successful!'); this.#router.navigate(['/home']); },
      error: () => this.#toast.error('Invalid or expired OTP.'),
      complete: () => this.loading = false
    });
  }

  backToLogin(): void { this.#router.navigate(['/login']); }
}
