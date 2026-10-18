import { Component, effect, inject, signal } from '@angular/core';
import { ToastType } from '../../models/enums/toast-type-enum';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-toast',
  standalone: true,
  imports: [],
  templateUrl: './toast.component.html',
  styleUrl: './toast.component.css',
})
export class ToastComponent {
  #toastService = inject(ToastService);
  #message = signal<string>('');
  #toastType = signal<ToastType>(ToastType.Primary);

  get message() {
    return this.#message();
  }

  get toastType() {
    return this.#toastType();
  }

  set message(value: string) {
    this.#message.set(value);
  }

  set toastType(value: ToastType) {
    this.#toastType.set(value);
  }

  constructor() {
    effect(() => {
      this.message = this.#toastService.message;
      this.toastType = this.#toastService.toastType;
    });
  }
}
