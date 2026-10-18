import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Project } from '../../models/project';

@Component({
  selector: 'app-dashboard',
  imports: [RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css',
})
export class DashboardComponent implements OnInit {
  #route = inject(ActivatedRoute);
  #router: Router = inject(Router);

  projects: Project[] = [];

  ngOnInit(): void {
    this.projects = this.#route.snapshot.data['projects'];

    if (this.projects?.length == 1) {
      this.openProject(this.projects[0]);
    }
  }

  openProject(project: Project) {
    this.#router.navigate([project.route]);
  }
}
