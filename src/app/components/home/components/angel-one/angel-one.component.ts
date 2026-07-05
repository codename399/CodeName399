import {
  Component,
  OnDestroy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';

import { CommonModule, DecimalPipe } from '@angular/common';

import { ToastService } from '../../../../services/toast.service';
import { AngelOneService } from '../../services/angel-one.service';
import { MarketService } from '../../services/market.service';

import { Gainer } from '../../models/gainer';
import { TradingConfiguration } from '../../models/trading-configuration';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-angel-one',

  standalone: true,

  imports: [CommonModule],

  templateUrl: './angel-one.component.html',

  styleUrls: ['./angel-one.component.css'],
})
export class AngelOneComponent implements OnInit, OnDestroy {
  readonly #angel = inject(AngelOneService);

  readonly #market = inject(MarketService);

  readonly #toast = inject(ToastService);

  readonly #router = inject(Router);

  // ======================================================
  // Dashboard State
  // ======================================================

  gainers = signal<Gainer[]>([]);

  availableCash = signal(0);

  marketStatus = signal('');

  marketTimer = signal('');

  searchText = signal('');

  // ======================================================
  // Configuration
  // ======================================================

  configuration = computed<TradingConfiguration | null>(() =>
    this.#angel.configuration(),
  );

  // ======================================================
  // UI
  // ======================================================

  showSummary = signal(false);

  showMarket = signal(false);

  showSettings = signal(false);

  showPortfolio = signal(false);

  showLogs = signal(false);

  private timerId: any;
  private subscription?: Subscription;

  // ======================================================
  // Computed
  // ======================================================

  filteredGainers = computed(() => {
    const search = this.searchText().trim().toLowerCase();

    const filtered = this.gainers().filter((stock) =>
      stock.symbol.toLowerCase().includes(search),
    );

    return filtered.sort((left, right) => {
      const leftBucket = this.getGainerSortBucket(left);
      const rightBucket = this.getGainerSortBucket(right);

      if (leftBucket !== rightBucket) {
        return leftBucket - rightBucket;
      }

      const scoreDiff = (right.score ?? 0) - (left.score ?? 0);

      if (scoreDiff !== 0) {
        return scoreDiff;
      }

      return left.symbol.localeCompare(right.symbol);
    });
  });

  // ======================================================
  // Computed Dashboard
  // ======================================================

  strategy = computed(() => this.configuration()?.strategy);

  autoTradingEnabled = computed(
    () => this.configuration()?.enableAutoTrading ?? false,
  );

  notificationsEnabled = computed(
    () => this.configuration()?.enableNotification ?? false,
  );

  riskPercentage = computed(() => this.configuration()?.riskPercentage ?? 0);

  maxDailyTrades = computed(() => this.configuration()?.maxDailyTrades ?? 0);

  // ======================================================
  // Lifecycle
  // ======================================================

  ngOnInit(): void {
    this.startMarketTimer();

    this.initializeDashboard();
  }

  ngOnDestroy(): void {
    if (this.timerId) {
      clearInterval(this.timerId);
    }

    this.subscription?.unsubscribe();
  }

  // ======================================================
  // Initialization
  // ======================================================

  private initializeDashboard(): void {
    this.loadDashboard();

    this.loadConfiguration();

    this.subscribeToGainers();
  }

  // ======================================================
  // Toolbar
  // ======================================================

  toggleSummary(): void {
    this.showSummary.update((v) => !v);
  }

  toggleMarket(): void {
    this.showMarket.update((v) => !v);
  }

  toggleSettings(): void {
    this.showSettings.update((v) => !v);
  }

  togglePortfolio(): void {
    this.showPortfolio.update((v) => !v);
  }

  toggleLogs(): void {
    this.showLogs.update((v) => !v);
  }

  openSettings(): void {
    this.#router.navigate(['/home/trading-settings']);
  }

  // ======================================================
  // Dashboard Summary
  // ======================================================

  private loadDashboard(): void {
    this.#angel

      .getDashboardSummary()

      .subscribe({
        next: (summary) => {
          this.availableCash.set(summary.availableCash);
        },

        error: (error) => {
          console.error(
            'Unable to load dashboard',

            error,
          );
        },
      });
  }

  // ======================================================
  // Trading Configuration
  // ======================================================

  private loadConfiguration(): void {
    this.#angel

      .getTradingConfiguration()

      .subscribe({
        next: () => {
          // Signal updated inside service.
        },

        error: (error) => {
          console.error(
            'Unable to load configuration',

            error,
          );

          this.#toast.error('Unable to load trading configuration');
        },
      });
  }

  saveConfiguration(): void {
    const configuration = this.configuration();

    if (!configuration) {
      return;
    }

    this.#angel

      .saveTradingConfiguration(configuration)

      .subscribe({
        next: () => {
          this.#toast.success('Configuration saved');
        },

        error: () => {
          this.#toast.error('Unable to save configuration');
        },
      });
  }

  // ======================================================
  // SignalR
  // ======================================================

  private async subscribeToGainers(): Promise<void> {
    await this.#market.startConnection();

    this.subscription = this.#market.gainers$.subscribe((data: Gainer[]) => {
      this.gainers.set(data);
    });
  }

  // ======================================================
  // Refresh
  // ======================================================

  refresh(): void {
    this.loadDashboard();

    this.loadConfiguration();
  }

  // ======================================================
  // Market Timer
  // ======================================================

  private startMarketTimer(): void {
    this.updateMarketTimer();

    this.timerId = setInterval(() => {
      this.updateMarketTimer();
    }, 1000);
  }

  private updateMarketTimer(): void {
    const now = new Date();

    const marketOpen = this.getTodayOpen();

    const marketClose = this.getTodayClose();

    if (this.isWeekend(now)) {
      this.marketStatus.set('CLOSED');

      this.marketTimer.set(
        `Opens in ${this.formatTime(
          this.getNextMarketOpen(now).getTime() - now.getTime(),
        )}`,
      );

      return;
    }

    // Before market

    if (now < marketOpen) {
      this.marketStatus.set('CLOSED');

      this.marketTimer.set(
        `Opens in ${this.formatTime(marketOpen.getTime() - now.getTime())}`,
      );

      return;
    }

    // During market

    if (now >= marketOpen && now < marketClose) {
      this.marketStatus.set('OPEN');

      this.marketTimer.set(
        `Closes in ${this.formatTime(marketClose.getTime() - now.getTime())}`,
      );

      return;
    }

    // After market

    this.marketStatus.set('CLOSED');

    this.marketTimer.set(
      `Opens in ${this.formatTime(
        this.getNextMarketOpen(now).getTime() - now.getTime(),
      )}`,
    );
  }

  // ======================================================
  // Helpers
  // ======================================================

  private getGainerSortBucket(stock: Gainer): number {
    if (stock.isOwned) {
      return 0;
    }

    const signal = (stock.signal ?? '').toUpperCase();

    if (signal === 'BUY' || signal === 'SETUP') {
      return 1;
    }

    return 2;
  }

  private getTodayOpen(): Date {
    const date = new Date();

    date.setHours(
      9,

      15,

      0,

      0,
    );

    return date;
  }

  private getTodayClose(): Date {
    const date = new Date();

    date.setHours(
      15,

      30,

      0,

      0,
    );

    return date;
  }

  private isWeekend(date: Date): boolean {
    return date.getDay() === 0 || date.getDay() === 6;
  }

  private getNextMarketOpen(current: Date): Date {
    const next = new Date(current);

    next.setHours(
      9,

      15,

      0,

      0,
    );

    do {
      next.setDate(next.getDate() + 1);
    } while (this.isWeekend(next));

    return next;
  }

  // ======================================================
  // Countdown
  // ======================================================

  private formatTime(milliseconds: number): string {
    let seconds = Math.max(
      0,

      Math.floor(milliseconds / 1000),
    );

    const days = Math.floor(seconds / 86400);

    seconds %= 86400;

    const hours = Math.floor(seconds / 3600);

    seconds %= 3600;

    const minutes = Math.floor(seconds / 60);

    seconds %= 60;

    const parts: string[] = [];

    if (days > 0) {
      parts.push(`${days}d`);
    }

    if (hours > 0 || days > 0) {
      parts.push(`${hours}h`);
    }

    parts.push(`${minutes}m`);

    parts.push(`${seconds}s`);

    return parts.join(' ');
  }
}
