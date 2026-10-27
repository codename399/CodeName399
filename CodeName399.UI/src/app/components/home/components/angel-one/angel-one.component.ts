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
  searchDraft = signal('');
  page = signal(1);
  pageSize = signal(10);
  totalCount = signal(0);
  serverPageGainers = signal<Gainer[]>([]);
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

  readonly visibleColumnSet = computed(() => new Set(['stock', ...this.visibleColumns()]));

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

  // SignalR is the sole source of gainer rows. Search and pagination are local.
  filteredGainers = computed(() => this.serverPageGainers());

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
    if (columnKey === 'stock' || !this.columnDefinitions.some((column) => column.key === columnKey)) {
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
          const configuredPageSize = Number(configuration?.gainersPageSize ?? 10);
          if (Number.isFinite(configuredPageSize) && configuredPageSize >= 1 && configuredPageSize <= 100 && configuredPageSize !== this.pageSize()) {
            this.pageSize.set(configuredPageSize);
            this.page.set(1);
            this.loadGainersPage();
          }
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
        ? ['stock', ...new Set(configuredColumns.filter((key: string) => key !== 'stock'))]
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
      visibleColumns: ['stock', ...visibleColumns.filter((key) => key !== 'stock')],
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
      const previous = this.gainers();
      const previousByToken = new Map<string, Gainer>(previous.map(stock => [String(stock.symbolToken), stock] as const));
      const snapshotByToken = new Map<string, Gainer>(snapshot.map(stock => [String(stock.symbolToken), stock] as const));
      const membershipChanged = previousByToken.size !== snapshotByToken.size
        || [...previousByToken.keys()].some(token => !snapshotByToken.has(token));
      const priorityChanged = snapshot.some(stock => {
        const old = previousByToken.get(String(stock.symbolToken));
        return !!old && (old.isOwned !== stock.isOwned
          || old.pullbackDetected !== stock.pullbackDetected
          || old.isManuallySubscribed !== stock.isManuallySubscribed
          || old.isSubscribed !== stock.isSubscribed);
      });
      this.gainers.set(snapshot);
      this.serverPageGainers.update(rows => this.sortVisibleRows(rows.map(row => ({ ...row, ...(snapshotByToken.get(String(row.symbolToken)) ?? {}) }))));
      this.isLoadingGainers.set(false);
      if (this.serverPageGainers().length === 0 || membershipChanged || priorityChanged) this.loadGainersPage();
    });

    await this.#market.startConnection();
    this.loadGainersPage();
  }

  // ======================================================
  // Refresh
  // ======================================================

  refresh(): void {
    this.showSettings.set(false);
    this.loadDashboard();
    this.loadConfiguration();
    // Refresh market rows through SignalR only; no REST gainers request.
    void this.#market.refreshGainersSnapshot();
  }

  onSearchDraftChanged(value: string): void {
    this.searchDraft.set(value);
  }

  applySearch(): void {
    this.searchText.set(this.searchDraft().trim());
    this.page.set(1);
    this.loadGainersPage();
  }

  changePage(nextPage: number): void {
    if (nextPage < 1 || nextPage > this.totalPages() || nextPage === this.page()) return;
    this.page.set(nextPage);
    this.loadGainersPage();
  }

  changePageSize(value: string): void {
    const size = Number(value);
    if (!Number.isFinite(size) || size < 1) return;
    this.pageSize.set(size);
    this.page.set(1);
    this.loadGainersPage();
  }

  private loadGainersPage(): void {
    // Gainers are delivered by MarketHub over SignalR. Keep search and
    // pagination entirely client-side so this component never calls
    // /api/market/gainers or /api/market/search.
    this.isLoadingGainers.set(this.gainers().length === 0);
    const query = this.searchText().trim().toLocaleLowerCase();
    const allRows = this.sortVisibleRows(this.gainers().filter(stock => {
      if (!query) return true;
      return String(stock.symbol ?? '').toLocaleLowerCase().includes(query);
    }));
    this.totalCount.set(allRows.length);
    const lastPage = Math.max(1, Math.ceil(allRows.length / this.pageSize()));
    if (this.page() > lastPage) this.page.set(lastPage);
    const start = (this.page() - 1) * this.pageSize();
    this.serverPageGainers.set(allRows.slice(start, start + this.pageSize()));
    this.isLoadingGainers.set(false);
  }

  openTrading(stock: Gainer): void {
    if (!stock?.symbolToken) return;
    this.#router.navigate(['/home/trade', stock.symbolToken], { queryParams: { symbol: stock.symbol, exchange: stock.exchange || 'NSE' } });
  }

  toggleSubscription(stock: Gainer): void {
    const token = String(stock.symbolToken ?? '');
    if (!token || this.actionInProgress()) return;
    this.actionInProgress.set(token);
    const exchange = stock.exchange || stock.quote?.exchange || 'NSE';
    const request = stock.isSubscribed ? this.#angel.unsubscribeStock(token, exchange) : this.#angel.subscribeStock(token, exchange);
    request.subscribe({
      next: () => {
        const updated = (row: Gainer) => row.symbolToken === stock.symbolToken
          ? { ...row, isSubscribed: !stock.isSubscribed, isManuallySubscribed: !stock.isSubscribed }
          : row;
        this.gainers.update(rows => rows.map(updated));
        this.serverPageGainers.update(rows => this.sortVisibleRows(rows.map(updated)));
        this.#toast.success(stock.isSubscribed ? `${stock.symbol} unsubscribed` : `${stock.symbol} subscribed`);
        this.actionInProgress.set(null);
        this.loadGainersPage();
      },
      error: error => {
        this.actionInProgress.set(null);
        this.#toast.error(error?.error?.message ?? `Unable to update subscription for ${stock.symbol}`);
      }
    });
  }

  executeManualOrder(stock: Gainer, side: 'buy' | 'sell', buyMode: 'minimum' | 'maximum' = 'minimum'): void {
    const token = String(stock.symbolToken ?? '');
    const quantity = side === 'sell' ? Number(stock.quantity ?? 0) : (buyMode === 'minimum' ? Number(stock.minimumBuyQuantity ?? 1) : 1);
    if (!token || quantity < 1 || this.actionInProgress()) {
      this.#toast.error(side === 'buy' ? 'Minimum buy quantity is not available for this stock.' : 'No owned quantity is available to sell.');
      return;
    }
    if (!this.isMarketOpenForOrders()) {
      this.#toast.error('Buy and sell are available only during market hours.');
      return;
    }
    this.actionInProgress.set(token);
    const exchange = stock.exchange || stock.quote?.exchange || 'NSE';
    const request = side === 'buy' ? this.#angel.buyStock(token, quantity, buyMode, exchange) : this.#angel.sellStock(token, quantity, exchange);
    request.subscribe({
      next: () => { this.actionInProgress.set(null); this.#toast.success(`${side.toUpperCase()} request submitted for ${stock.symbol}`); this.loadDashboard(); this.loadGainersPage(); },
      error: error => { this.actionInProgress.set(null); this.#toast.error(error?.error?.message ?? `Unable to ${side} ${stock.symbol}`); }
    });
  }

  isMarketOpenForOrders(): boolean {
    if (this.configuration()?.ignoreMarketHours) return true;
    const now = new Date();
    return !this.isNonTradingDay(now) && now >= this.getTodayOpen() && now < this.getTodayClose();
  }

  hasSufficientFunds(stock: Gainer): boolean {
    if (stock.sufficientFunds != null) return stock.sufficientFunds;
    const investment = stock.minimumInvestmentWithCharges ?? ((stock.minimumBuyQuantity ?? 0) * stock.currentPrice + (stock.estimatedCharges ?? 0));
    return investment > 0 && this.availableCash() >= investment;
  }

  displayProfitLoss(stock: Gainer): number {
    const gross = Number(stock.profitLoss ?? 0);
    const charges = Number(stock.ownedEstimatedCharges ?? 0);
    return stock.isOwned && charges > 0 ? gross - charges : gross;
  }

  isDisplayProfitPositive(stock: Gainer): boolean {
    return this.displayProfitLoss(stock) >= 0;
  }

  displayCharges(stock: Gainer): string {
    if (stock.isOwned) {
      const charges = Number(stock.ownedEstimatedCharges ?? 0);
      return charges > 0 ? this.formatCurrency(charges) : 'Calculating…';
    }
    return stock.estimatedCharges != null ? this.formatCurrency(stock.estimatedCharges) : '—';
  }

  displayMinimumInvestment(stock: Gainer): string {
    if (stock.isOwned || stock.minimumInvestmentWithCharges == null) return '—';
    return this.formatCurrency(stock.minimumInvestmentWithCharges);
  }

  private formatCurrency(value: number): string {
    return `₹${new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value)}`;
  }

  hasBudgetForMaximum(stock: Gainer): boolean {
    const cash = this.availableCash();
    const allocationPercent = Math.min(100, Math.max(0, Number(this.configuration()?.maxCapitalPerTradePercent ?? 0)));
    const budget = cash * allocationPercent / 100;
    return stock.currentPrice > 0 && budget >= stock.currentPrice;
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
      if (this.isNonTradingDay(now)) {
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

    if (this.isNonTradingDay(now)) {
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

  private normalizeGainerForDisplay(stock: Gainer): Gainer {
    const signal = String(stock.signal ?? '').trim().toUpperCase();
    if (!stock.isOwned && signal === 'SELL') {
      return { ...stock, signal: 'HOLD' };
    }
    return stock;
  }

  private getGainerSortBucket(stock: Gainer): number {
    if (stock.isOwned) return 0;
    if (stock.pullbackDetected === true) return 1;
    if (stock.isManuallySubscribed) return 2;
    const signal = (stock.signal ?? '').toUpperCase();
    if (signal === 'BUY' || signal === 'SETUP') return 3;
    return 4;
  }

  private sortVisibleRows(rows: Gainer[]): Gainer[] {
    return [...rows].sort((a, b) =>
      this.getGainerSortBucket(a) - this.getGainerSortBucket(b)
      || Number(b.changePercent ?? 0) - Number(a.changePercent ?? 0)
      || String(a.symbol ?? '').localeCompare(String(b.symbol ?? '')),
    );
  }

  private getTodayOpen(): Date {
    return this.getIndiaDateAtTime(this.configuration()?.marketOpenTime ?? '09:15:00', new Date(), 0);
  }

  private getTodayClose(): Date {
    return this.getIndiaDateAtTime(this.configuration()?.marketCloseTime ?? '15:00:00', new Date(), 0);
  }

  private indiaDateParts(date: Date): { year: number; month: number; day: number; weekday: string } {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Kolkata', year: 'numeric', month: '2-digit', day: '2-digit', weekday: 'short',
    }).formatToParts(date);
    const get = (type: string) => parts.find(part => part.type === type)?.value ?? '';
    return { year: Number(get('year')), month: Number(get('month')), day: Number(get('day')), weekday: get('weekday') };
  }

  private indiaDateKey(date: Date): string {
    const p = this.indiaDateParts(date);
    return `${p.year}-${String(p.month).padStart(2, '0')}-${String(p.day).padStart(2, '0')}`;
  }

  private getIndiaDateAtTime(time: string, reference: Date, dayOffset: number): Date {
    const p = this.indiaDateParts(reference);
    const calendarDate = new Date(Date.UTC(p.year, p.month - 1, p.day + dayOffset));
    const [hour, minute, second] = String(time).split(':').map(Number);
    // Build an absolute instant from an IST wall-clock value without relying on
    // the browser/host machine's local timezone.
    return new Date(Date.UTC(calendarDate.getUTCFullYear(), calendarDate.getUTCMonth(), calendarDate.getUTCDate(), hour || 0, minute || 0, second || 0) - 330 * 60 * 1000);
  }

  private isWeekend(date: Date): boolean {
    return ['Sat', 'Sun'].includes(this.indiaDateParts(date).weekday);
  }

  private isNonTradingDay(date: Date): boolean {
    const config = this.configuration();
    return this.isWeekend(date) || (config?.tradingHolidays ?? []).includes(this.indiaDateKey(date));
  }

  private getNextMarketOpen(current: Date): Date {
    const openTime = this.configuration()?.marketOpenTime ?? '09:15:00';
    for (let offset = 1; offset <= 31; offset++) {
      const candidate = this.getIndiaDateAtTime(openTime, current, offset);
      if (!this.isNonTradingDay(candidate)) return candidate;
    }
    return this.getIndiaDateAtTime(openTime, current, 1);
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
