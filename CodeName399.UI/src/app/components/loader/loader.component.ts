import { Component, effect, inject, signal } from '@angular/core';
import { LoaderService } from '../../services/loader.service';

@Component({
  selector: 'app-loader',
  imports: [],
  templateUrl: './loader.component.html',
  styleUrl: './loader.component.css'
})
export class LoaderComponent {
#loaderService = inject(LoaderService);
#isLoading= signal<boolean>(false);

  get isLoading() {
    return this.#isLoading();
  }

  constructor() {
    effect(() => {
      this.#isLoading.set(this.#loaderService.isLoading());
    });
  }

  showLoader() {
    this.#loaderService.show();
  }

  hideLoader() {
    this.#loaderService.hide();
  }
}
