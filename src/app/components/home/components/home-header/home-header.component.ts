import { Component, HostListener, inject, Input } from '@angular/core';
import { Router } from '@angular/router';
import { Config } from '../../../../../assets/environments/config';
import { AuthenticationService } from '../../../authentication/services/authentication-service';
import { User } from '../../models/user';
import { LogsComponent } from '../logs/logs.component';
import { Constants } from '../../../../../constants';

@Component({
  selector: 'app-home-header',
  standalone: true,
  imports: [LogsComponent],
  templateUrl: './home-header.component.html',
  styleUrl: './home-header.component.css'
})
export class HomeHeaderComponent {
  #authenticationService = inject(AuthenticationService);
  #router = inject(Router);
  #config = inject(Config);

  get user(): User {
    return this._user;
  }

  @Input("user") set user(value: User) {
    this._user = value;

    if (this.user) {
      this.#authenticationService.user = this.user;

      if (this.user.profilePicture) {
        this.profilePictureUrl = this.user.profilePicture ?? "";
      }
    }
  }

  private _user!: User;
  profilePictureUrl!: string;
  logoUrl!: string
  isDropdownOpen: boolean = false;
  isLogsOpen: boolean = false;

  get isAdminUser(): boolean {
    return this.#authenticationService.hasClaim(Constants.roleClaim, Constants.admin);
  }

  constructor() {
    this.profilePictureUrl = this.#config.profilePictureUrl;
  }

  toggleDropdown() {
    this.isDropdownOpen = !this.isDropdownOpen;
  }

  logout() {
    this.#authenticationService.logout();
  }

  openLogs() {
    this.isLogsOpen = true;
    this.isDropdownOpen = false;
  }

  closeLogs() {
    this.isLogsOpen = false;
  }

  changePassword() {
    this.#router.navigate(['/home/change-password']);
  }

  editProfile() {
    this.#router.navigate(['/home/register', this.user.id]);
  }

  goHome() {
    this.#router.navigate(['/home']);
  }

  // Optional: Close dropdown when clicking outside
  @HostListener('document:click', ['$event'])
  onClickOutside(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (!target.closest('.relative')) {
      this.isDropdownOpen = false;
    }
  }
}
