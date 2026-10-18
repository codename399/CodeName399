import { AbstractControl, FormControl, FormGroup } from '@angular/forms';

export const isInvalid = (control: any): boolean | null => {
  return control && control.touched && control.invalid;
};

export const getErrorMessage = (control: any): string | null => {

  if (control && control.errors) {
    if (control.errors['required']) {
      return `*Field is required`;
    }
    if (control.errors['minlength']) {
      return `*Field must be at least ${control.errors['minlength'].requiredLength} characters long`;
    }
    if (control.errors['maxlength']) {
      return `*Field cannot exceed ${control.errors['maxlength'].requiredLength} characters`;
    }
    if (control.errors['pattern']) {
      return `*Invalid format for field`;
    }
    if (control.errors['email']) {
      return `*Invalid Email ID`;
    }
    if (control.errors['passwordMismatch']) {
      return `*Passwords do not match`;
    }
  }
  return null;
};