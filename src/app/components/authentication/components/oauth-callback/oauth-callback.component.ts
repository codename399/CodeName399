import { Component, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthenticationService } from '../../services/authentication-service';

@Component({
  selector: 'app-oauth-callback',
  standalone: true,
  template: `<div class="oauth-callback">Completing sign in…</div>`
})
export class OAuthCallbackComponent {
  #route = inject(ActivatedRoute);
  #router = inject(Router);
  #auth = inject(AuthenticationService);

  constructor() {
    const fragment = this.#route.snapshot.fragment ?? '';
    const params = new URLSearchParams(fragment);

    const token = params.get('token');
    const refreshToken = params.get('refreshToken');
    const userId = params.get('userId');

    if (!token || !refreshToken || !userId) {
      this.#router.navigate(['/login']);
      return;
    }

    this.#auth.token = token;
    this.#auth.refreshToken = refreshToken;
    this.#auth.userId = userId;
    this.#router.navigate(['/home']);
  }
}
