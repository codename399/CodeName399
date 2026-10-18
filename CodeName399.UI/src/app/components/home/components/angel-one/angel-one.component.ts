import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  Renderer2,
  ViewChild,
  ChangeDetectionStrategy,
  computed,
  inject,
  signal,
} from '@angular/core';

import { CommonModule, DecimalPipe } from '@angular/common';

import { ToastService } from '../../../../services/toast.service';
import { AngelOneService } from '../../services/angel-one.service';
import { MarketService } from '../../services/market.service';

import { Gainer } from '../../models/gainer';
import { TradingConfiguration } from '../../models/trading-settings';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-angel-one',

  standalone: true,

  imports: [CommonModule],

  templateUrl: './angel-one.component.html',

  styleUrls: ['./angel-one.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AngelOneComponent implements OnInit, AfterViewInit, OnDestroy {
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
  page = signal(1);
  pageSize = signal(10);
  totalCount = computed(() => this.getMatchingGainers().length);
  isLoadingGainers = signal(false);
  actionInProgress = signal<string | null>(null);

  // ======================================================
  // Configuration
  // ======================================================

  configuration = computed<TradingConfiguration | null>(() =>
    this.#angel.configuration(),
  );

  // ======================================================
  // UI
  // ======================================================

  showSettings = signal(false);

  showPortfolio = signal(false);

  showLogs = signal(false);

  @ViewChild('floatingToggle', { static: true })
  private floatingToggle?: ElementRef<HTMLButtonElement>;

  readonly #renderer = inject(Renderer2);

  #footerResizeObserver?: ResizeObserver;

  readonly #positionFloatingToggle = (): void => {
    const button = this.floatingToggle?.nativeElement;
    const footer = document.querySelector(
      'app-home-footer .home-footer',
    ) as HTMLElement | null;

    if (!button || !footer) {
      return;
    }

    const footerRect = footer.getBoundingClientRect();
    const buttonHeight = button.getBoundingClientRect().height || 46;
    const gap = 12;

    // The button's bottom edge is placed exactly `gap` pixels above the
    // rendered footer's top edge. This is based on the actual viewport
    // geometry, not a guessed mobile footer height.
    const bottom = Math.max(
      8,
      Math.ceil(window.innerHeight - footerRect.top + gap),
    );

    button.style.setProperty('position', 'fixed', 'important');
    button.style.setProperty('top', 'auto', 'important');
    button.style.setProperty('left', 'auto', 'important');
    button.style.setProperty('right', '12px', 'important');
    button.style.setProperty('bottom', `${bottom}px`, 'important');
    button.style.setProperty('z-index', '2147483647', 'important');
    button.style.setProperty('transform', 'none', 'important');
    button.style.setProperty('visibility', 'visible', 'important');
    button.style.setProperty('opacity', '1', 'important');
  };

  // Only expose columns that belong to the current gainer grid.
  readonly columnDefinitions = [
    { key: 'stock', label: 'Stock', defaultVisible: true },
    { key: 'ltp', label: 'LTP', defaultVisible: true },
    { key: 'minimumQuantity', label: 'Minimum Quantity', defaultVisible: true },
    { key: 'charges', label: 'Charges', defaultVisible: true },
    { key: 'investmentWithCharges', label: 'Investment + Charges', defaultVisible: true },
    { key: 'profitLoss', label: 'P/L if Owned', defaultVisible: true },
    { key: 'pullback', label: 'Pullback', defaultVisible: true },
    { key: 'funds', label: 'Available Funds', defaultVisible: true },
    { key: 'subscription', label: 'Subscription', defaultVisible: true },
    { key: 'action', label: 'Buy / Sell', defaultVisible: true },
  ];

  visibleColumns = signal<string[]>([]);

  readonly visibleColumnSet = computed(() => new Set(this.visibleColumns()));

  private timerId: any;
  private subscription?: Subscription;

  // ======================================================
  // Computed
  // ======================================================

  subscribedGainers = computed(() =>
    this.gainers().filter((stock) => stock.isSubscribed === true),
  );

  isWaitingForSubscriptions = computed(
    () => this.subscribedGainers().length === 0,
  );

  // SignalR owns the complete list. Search and pagination are performed locally;
  // no REST request is made to load or page gainers.
  filteredGainers = computed(() => {
    const matching = this.getMatchingGainers().sort((a, b) =>
      this.getGainerSortBucket(a) - this.getGainerSortBucket(b) ||
      String(a.symbol ?? '').localeCompare(String(b.symbol ?? ''))
    );
    const start = (this.page() - 1) * this.pageSize();
    return matching.slice(start, start + this.pageSize());
  });

  totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
  firstRowNumber = computed(() => this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1);
  lastRowNumber = computed(() => Math.min(this.page() * this.pageSize(), this.totalCount()));

  // ======================================================
  // Computed Dashboard
  // ======================================================

  autoTradingEnabled = computed(
    () => this.configuration()?.enableAutoTrading ?? false,
  );

  notificationsEnabled = computed(
    () => this.configuration()?.enableNotification ?? false,
  );

  activeInstrumentType = computed(() => 'Equity' as const);

  riskPercentage = computed(
    () => this.configuration()?.riskPercentage ?? 0,
  );

  // ======================================================
  // Lifecycle
  // ======================================================

  ngOnInit(): void {
    this.startMarketTimer();

    this.initializeDashboard();
  }

  ngAfterViewInit(): void {
    const button = this.floatingToggle?.nativeElement;

    if (button && button.parentElement !== document.body) {
      this.#renderer.appendChild(document.body, button);
    }

    // Calculate the position from the REAL footer bounds rather than a
    // hard-coded mobile/desktop offset.
    requestAnimationFrame(() => this.#positionFloatingToggle());

    window.addEventListener('resize', this.#positionFloatingToggle, {
      passive: true,
    });

    const footer = document.querySelector('app-home-footer .home-footer');

    if (footer) {
      this.#footerResizeObserver = new ResizeObserver(() => {
        this.#positionFloatingToggle();
      });

      this.#footerResizeObserver.observe(footer);
    }
  }

  ngOnDestroy(): void {
    window.removeEventListener('resize', this.#positionFloatingToggle);
    this.#footerResizeObserver?.disconnect();

    const button = this.floatingToggle?.nativeElement;

    if (button?.parentElement === document.body) {
      this.#renderer.removeChild(document.body, button);
    }

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
    if (!this.columnDefinitions.some((column) => column.key === columnKey)) {
      return;
    }
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
    return this.visibleColumnSet().has(columnKey);
  }

  trackByStock = (_index: number, stock: Gainer): string =>
    String(stock.symbolToken ?? stock.symbol ?? _index);

  trackByColumn = (_index: number, column: { key: string }): string =>
    column.key;

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
          // Close the left controls panel only after the columns have been
          // successfully saved. Keep it open if the save fails so the user
          // can correct/retry without losing context.
          this.showSettings.set(false);
        },

        error: () => {
          this.#toast.error('Unable to save configuration');
        },
      });
  }

  private applyVisibleColumns(
    configuration: TradingConfiguration | null,
  ): void {
    // Ignore legacy saved keys (EMA, stop-loss, target, recovery, etc.).
    const allowedKeys = new Set(this.columnDefinitions.map((column) => column.key));
    const configuredColumns = (configuration?.['visibleColumns'] ?? [])
      .filter((columnKey: string) => allowedKeys.has(columnKey));

    this.visibleColumns.set(
      configuredColumns.length
        ? [...new Set(configuredColumns)]
        : this.getDefaultVisibleColumnKeys(),
    );
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
    // Subscribe BEFORE starting SignalR. MarketHub sends the current watchlist
    // from OnConnectedAsync; subscribing afterwards can miss that one-shot
    // snapshot and leave the dashboard empty until the next market update.
    this.subscription?.unsubscribe();
    this.isLoadingGainers.set(true);
    this.subscription = this.#market.gainers$.subscribe((data: Gainer[] | null) => {
      if (data === null) return; // SignalR has not delivered its initial snapshot yet.
      const snapshot = data.map((stock) => this.normalizeGainerForDisplay(stock));
      this.gainers.set(snapshot);
      this.isLoadingGainers.set(false);
      const lastPage = Math.max(1, Math.ceil(this.totalCount() / this.pageSize()));
      if (this.page() > lastPage) this.page.set(lastPage);
    });

    await this.#market.startConnection();
  }

  // ======================================================
  // Refresh
  // ======================================================

  refresh(): void {
    this.showSettings.set(false);
    this.loadDashboard();
    this.loadConfiguration();
    // Reconnect to trigger the hub's OnConnectedAsync snapshot. Gainers are
    // never fetched through REST.
    void this.#market.refreshGainersSnapshot();
  }

  onSearchChanged(value: string): void {
    this.searchText.set(value);
    this.page.set(1);
  }

  changePage(nextPage: number): void {
    if (nextPage < 1 || nextPage > this.totalPages() || nextPage === this.page()) return;
    this.page.set(nextPage);
  }

  changePageSize(value: string): void {
    const size = Number(value);
    if (!Number.isFinite(size) || size < 1) return;
    this.pageSize.set(size);
    this.page.set(1);
  }

  toggleSubscription(stock: Gainer): void {
    const token = String(stock.symbolToken ?? '');
    if (!token || this.actionInProgress()) return;
    this.actionInProgress.set(token);
    const request = stock.isSubscribed ? this.#angel.unsubscribeStock(token) : this.#angel.subscribeStock(token);
    request.subscribe({
      next: () => {
        this.gainers.update(rows => rows.map(row => row.symbolToken === stock.symbolToken ? { ...row, isSubscribed: !stock.isSubscribed } : row));
        this.#toast.success(stock.isSubscribed ? `${stock.symbol} unsubscribed` : `${stock.symbol} subscribed`);
        this.actionInProgress.set(null);
      },
      error: error => {
        this.actionInProgress.set(null);
        this.#toast.error(error?.error?.message ?? `Unable to update subscription for ${stock.symbol}`);
      }
    });
  }

  executeManualOrder(stock: Gainer, side: 'buy' | 'sell'): void {
    const token = String(stock.symbolToken ?? '');
    const quantity = side === 'sell' ? Number(stock.quantity ?? 0) : Number(stock.minimumBuyQuantity ?? 0);
    if (!token || quantity < 1 || this.actionInProgress()) {
      this.#toast.error(side === 'buy' ? 'Minimum buy quantity is not available for this stock.' : 'No owned quantity is available to sell.');
      return;
    }
    if (!this.isMarketOpenForOrders()) {
      this.#toast.error('Buy and sell are available only during market hours.');
      return;
    }
    this.actionInProgress.set(token);
    const request = side === 'buy' ? this.#angel.buyStock(token, quantity) : this.#angel.sellStock(token, quantity);
    request.subscribe({
      next: () => { this.actionInProgress.set(null); this.#toast.success(`${side.toUpperCase()} request submitted for ${stock.symbol}`); this.loadDashboard(); },
      error: error => { this.actionInProgress.set(null); this.#toast.error(error?.error?.message ?? `Unable to ${side} ${stock.symbol}`); }
    });
  }

  isMarketOpenForOrders(): boolean {
    if (this.configuration()?.ignoreMarketHours) return true;
    const now = new Date();
    return !this.isWeekend(now) && now >= this.getTodayOpen() && now < this.getTodayClose();
  }

  hasSufficientFunds(stock: Gainer): boolean {
    if (stock.sufficientFunds != null) return stock.sufficientFunds;
    const investment = stock.minimumInvestmentWithCharges ?? ((stock.minimumBuyQuantity ?? 0) * stock.currentPrice + (stock.estimatedCharges ?? 0));
    return investment > 0 && this.availableCash() >= investment;
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

  private getMatchingGainers(): Gainer[] {
    const search = this.searchText().trim().toLocaleLowerCase();
    const rows = this.gainers();
    if (!search) return rows;
    return rows.filter(stock =>
      String(stock.symbol ?? '').toLocaleLowerCase().includes(search) ||
      String(stock.exchange ?? '').toLocaleLowerCase().includes(search) ||
      String(stock.symbolToken ?? '').toLocaleLowerCase().includes(search)
    );
  }

  private normalizeGainerForDisplay(stock: Gainer): Gainer {
    const signal = String(stock.signal ?? '').trim().toUpperCase();
    if (!stock.isOwned && signal === 'SELL') {
      return { ...stock, signal: 'HOLD' };
    }
    return stock;
  }

  private getGainerSortBucket(stock: Gainer): number {
    if (stock.isOwned) {
      return 0;
    }

    if (stock.isSubscribed) {
      return 1;
    }

    if (stock.pullbackDetected === true) {
      return 2;
    }

    const signal = (stock.signal ?? '').toUpperCase();
    if (signal === 'BUY' || signal === 'SETUP') return 3;
    return 4;
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
      15, 0,
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
