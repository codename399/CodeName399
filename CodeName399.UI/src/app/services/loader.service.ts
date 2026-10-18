import { Injectable, signal } from '@angular/core';

@Injectable({
  providedIn: 'root',
})
export class LoaderService {
  #loading = signal<boolean>(false);

  get loading() {
    return this.#loading();
  }

  set loading(value: boolean) {
    this.#loading.set(value);
  }

  show() {
    this.#loading.set(true);
  }

  hide() {
    this.#loading.set(false);
  }

  isLoading(): boolean {
    return this.#loading();
  }
}
