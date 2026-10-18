import { ValidatorFn } from '@angular/forms';

export const PasswordMatchValidator: ValidatorFn = (control) => {
  const password = control.get('password');
  const confirmPassword = control.get('confirmPassword');

  if (password && confirmPassword && password.value !== confirmPassword.value) {
    confirmPassword.setErrors({ passwordMismatch: true });
    return { passwordMismatch: true };
  }

  return null;
};
