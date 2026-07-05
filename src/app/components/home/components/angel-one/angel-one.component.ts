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

  readonly columnDefinitions = [
    { key: 'star', label: '⭐', defaultVisible: true },
    { key: 'symbol', label: 'Symbol', defaultVisible: true },
    { key: 'token', label: 'Token', defaultVisible: false },
    { key: 'ltp', label: 'LTP', defaultVisible: true },
    { key: 'prevClose', label: 'Prev Close', defaultVisible: true },
    { key: 'change', label: 'Change', defaultVisible: true },
    { key: 'vwap', label: 'VWAP', defaultVisible: false },
    { key: 'ema9', label: 'EMA9', defaultVisible: false },
    { key: 'ema21', label: 'EMA21', defaultVisible: false },
    { key: 'rsi', label: 'RSI', defaultVisible: false },
    { key: 'volumeMultiplier', label: 'Vol×', defaultVisible: false },
    { key: 'pullbackDistance', label: 'PB%', defaultVisible: false },
    { key: 'quantity', label: 'Qty', defaultVisible: false },
    { key: 'averagePrice', label: 'Avg', defaultVisible: false },
    { key: 'investedAmount', label: 'Invested', defaultVisible: false },
    { key: 'currentValue', label: 'Current', defaultVisible: false },
    { key: 'profitLoss', label: 'P/L', defaultVisible: false },
    { key: 'score', label: 'Score', defaultVisible: true },
    { key: 'signal', label: 'Signal', defaultVisible: true },
    { key: 'risk', label: 'Risk', defaultVisible: true },
    { key: 'stopLoss', label: 'SL', defaultVisible: true },
    { key: 'targetPrice', label: 'Target', defaultVisible: true },
    { key: 'atr', label: 'ATR', defaultVisible: false },
    { key: 'estimatedGrossProfit', label: 'Gross', defaultVisible: false },
    { key: 'estimatedCharges', label: 'Charges', defaultVisible: false },
    { key: 'estimatedNetProfit', label: 'Net', defaultVisible: false },
    { key: 'chargesPercent', label: 'Charges %', defaultVisible: false },
    { key: 'chargesAccepted', label: 'Charges OK', defaultVisible: false },
    { key: 'chargesReason', label: 'Charge Reason', defaultVisible: false },
    { key: 'reason', label: 'Reason', defaultVisible: true },
  ];

  visibleColumns = signal<string[]>([]);

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

  toggleColumn(columnKey: string): void {
    const current = this.visibleColumns();
    const next = current.includes(columnKey)
      ? current.filter((key) => key !== columnKey)
      : [...current, columnKey];

    this.visibleColumns.set(next);
    this.persistVisibleColumns(next);
  }

  resetColumns(): void {
    const defaults = this.getDefaultVisibleColumnKeys();

    this.visibleColumns.set(defaults);
    this.persistVisibleColumns(defaults);
  }

  isColumnVisible(columnKey: string): boolean {
    return this.visibleColumns().includes(columnKey);
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
        next: (configuration) => {
          this.applyVisibleColumns(configuration);
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

    const payload: TradingConfiguration = {
      ...configuration,
      visibleColumns: this.visibleColumns(),
    };

    this.#angel

      .saveTradingConfiguration(payload)

      .subscribe({
        next: () => {
          this.#toast.success('Configuration saved');
        },

        error: () => {
          this.#toast.error('Unable to save configuration');
        },
      });
  }

  private applyVisibleColumns(configuration: TradingConfiguration | null): void {
    const configuredColumns = configuration?.visibleColumns?.filter((columnKey) =>
      this.columnDefinitions.some((column) => column.key === columnKey),
    );

    const nextColumns = configuredColumns?.length
      ? configuredColumns
      : this.getDefaultVisibleColumnKeys();

    this.visibleColumns.set(nextColumns);
  }

  private persistVisibleColumns(visibleColumns: string[]): void {
    const configuration = this.configuration();

    if (!configuration) {
      return;
    }

    const payload: TradingConfiguration = {
      ...configuration,
      visibleColumns,
    };

    this.#angel.saveTradingConfiguration(payload).subscribe({
      error: (error) => {
        console.error('Unable to persist visible columns', error);
      },
    });
  }

  private getDefaultVisibleColumnKeys(): string[] {
    return this.columnDefinitions
      .filter((column) => column.defaultVisible)
      .map((column) => column.key);
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
    const config = this.configuration();

    if (!config) {
      this.marketStatus.set('LOADING');
      this.marketTimer.set('');
      return;
    }

    const now = new Date();

    const marketOpen = this.getTodayOpen();
    const marketClose = this.getTodayClose();

    if (config.ignoreMarketHours) {
      if (this.isWeekend(now)) {
        this.marketStatus.set('OPEN (Ignored)');
        this.marketTimer.set(
          `Market opens in ${this.formatTime(
            this.getNextMarketOpen(now).getTime() - now.getTime(),
          )}`,
        );
        return;
      }

      if (now < marketOpen) {
        this.marketStatus.set('OPEN (Ignored)');
        this.marketTimer.set(
          `Market opens in ${this.formatTime(
            marketOpen.getTime() - now.getTime(),
          )}`,
        );
        return;
      }

      if (now >= marketOpen && now < marketClose) {
        this.marketStatus.set('OPEN');
        this.marketTimer.set(
          `Market closes in ${this.formatTime(
            marketClose.getTime() - now.getTime(),
          )}`,
        );
        return;
      }

      this.marketStatus.set('OPEN (Ignored)');
      this.marketTimer.set(
        `Market opens in ${this.formatTime(
          this.getNextMarketOpen(now).getTime() - now.getTime(),
        )}`,
      );
      return;
    }

    if (this.isWeekend(now)) {
      this.marketStatus.set('CLOSED');

      this.marketTimer.set(
        `Opens in ${this.formatTime(
          this.getNextMarketOpen(now).getTime() - now.getTime(),
        )}`,
      );

      return;
    }

    if (now < marketOpen) {
      this.marketStatus.set('CLOSED');

      this.marketTimer.set(
        `Opens in ${this.formatTime(marketOpen.getTime() - now.getTime())}`,
      );

      return;
    }

    if (now >= marketOpen && now < marketClose) {
      this.marketStatus.set('OPEN');

      this.marketTimer.set(
        `Closes in ${this.formatTime(marketClose.getTime() - now.getTime())}`,
      );

      return;
    }

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
    const config = this.configuration();

    const date = new Date();

    const [hour, minute] = config?.marketOpenTime?.split(':').map(Number) ?? [
      9, 15,
    ];

    date.setHours(hour, minute, 0, 0);

    return date;
  }

  private getTodayClose(): Date {
    const config = this.configuration();

    const date = new Date();

    const [hour, minute] = config?.marketCloseTime?.split(':').map(Number) ?? [
      15, 30,
    ];

    date.setHours(hour, minute, 0, 0);

    return date;
  }

  private isWeekend(date: Date): boolean {
    return date.getDay() === 0 || date.getDay() === 6;
  }

  private getNextMarketOpen(current: Date): Date {
    const config = this.configuration();

    const next = new Date(current);

    const [hour, minute] = config?.marketOpenTime?.split(':').map(Number) ?? [
      9, 15,
    ];

    next.setHours(hour, minute, 0, 0);

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
