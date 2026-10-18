import { Component, inject, OnInit } from "@angular/core";
import { AuthenticationService } from "../../services/authentication-service";

@Component({
  selector: 'app-logout-component',
  template: ''
})
export class LogoutComponent implements OnInit {
  #authenticationService = inject(AuthenticationService);
  
  ngOnInit(): void {
    this.#authenticationService.logout();
  }
}
