import { CommonModule } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { Subscription, catchError, interval, of, startWith, switchMap } from 'rxjs';

import { Project } from '../../models/project';
import { TradingOptimizationStatus } from '../../models/trading-optimization-status';
import { AngelOneService } from '../../services/angel-one.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css',
})
export class DashboardComponent implements OnInit, OnDestroy {
  #route = inject(ActivatedRoute);
  #router = inject(Router);
  #angel = inject(AngelOneService);

  projects: Project[] = [];
  optimizationStatus: TradingOptimizationStatus | null = null;
  optimizationAvailable = false;
  #statusSubscription?: Subscription;

  /**
   * Dashboard appearance is shared with Angel One and Trading Configuration.
   * Light is the default; Dark is only selected when explicitly persisted.
   */
  theme = signal<'dark' | 'light'>(
    typeof localStorage !== 'undefined' &&
      localStorage.getItem('codename399-theme') === 'dark'
      ? 'dark'
      : 'light',
  );

  setTheme(theme: 'dark' | 'light'): void {
    this.theme.set(theme);

    if (typeof localStorage !== 'undefined') {
      localStorage.setItem('codename399-theme', theme);
    }
  }

  toggleTheme(): void {
    this.setTheme(this.theme() === 'dark' ? 'light' : 'dark');
  }

  ngOnInit(): void {
    this.projects = this.#route.snapshot.data['projects'] ?? [];

    // Preserve the existing single-project behaviour.
    if (this.projects.length === 1) {
      this.openProject(this.projects[0]);
      return;
    }

    this.startOptimizationStatusPolling();
  }

  private startOptimizationStatusPolling(): void {
    this.#statusSubscription = interval(10000)
      .pipe(
        startWith(0),
        switchMap(() =>
          this.#angel.getTradingOptimizationStatus().pipe(
            catchError(() => of(null)),
          ),
        ),
      )
      .subscribe((status) => {
        this.optimizationStatus = status;
        this.optimizationAvailable = status !== null;
      });
  }

  get activeProjectCount(): number {
    return this.projects.filter((project) => !!project.route).length;
  }

  get tradingProject(): Project | undefined {
    return this.projects.find((project) => {
      const value = `${project.name ?? ''} ${project.route ?? ''}`.toLowerCase();
      return value.includes('angel') || value.includes('kite') || value.includes('trad');
    });
  }

  get optimizerState(): string {
    if (!this.optimizationAvailable) return 'OFFLINE';
    if (!this.optimizationStatus?.enabled) return 'DISABLED';
    if (this.optimizationStatus.candidate) return 'TESTING';
    return 'READY';
  }

  get optimizerPnl(): number {
    return this.optimizationStatus?.candidate?.metrics?.netProfit ?? 0;
  }

  openProject(project: Project): void {
    if (project.route) {
      this.#router.navigate([project.route]);
    }
  }

  trackProject(index: number, project: Project): string | number {
    return project.id ?? project.route ?? index;
  }

  ngOnDestroy(): void {
    this.#statusSubscription?.unsubscribe();
  }
}
