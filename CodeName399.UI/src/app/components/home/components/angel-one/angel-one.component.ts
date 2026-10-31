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

  quotes = signal<Gainer[]>([]);

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

  readonly columnDefinitions = [
    { key: 'star', label: '⭐', defaultVisible: true },
    { key: 'symbol', label: 'Symbol', defaultVisible: true },
    { key: 'token', label: 'Token', defaultVisible: false },
    { key: 'exchange', label: 'Exchange', defaultVisible: false },
    { key: 'prevClose', label: 'Prev Close', defaultVisible: true },
    { key: 'ltp', label: 'LTP', defaultVisible: true },
    { key: 'priceChange', label: 'Change %', defaultVisible: true },
    { key: 'bid', label: 'Bid', defaultVisible: false },
    { key: 'ask', label: 'Ask', defaultVisible: false },
    { key: 'open', label: 'Open', defaultVisible: false },
    { key: 'high', label: 'High', defaultVisible: false },
    { key: 'low', label: 'Low', defaultVisible: false },
    { key: 'volume', label: 'Volume', defaultVisible: false },
    { key: 'quantity', label: 'Qty', defaultVisible: true },
    { key: 'averagePrice', label: 'Avg Price', defaultVisible: true },
    { key: 'investedAmount', label: 'Invested', defaultVisible: true },
    { key: 'currentValue', label: 'Current Value', defaultVisible: true },
    { key: 'profitLoss', label: 'P/L', defaultVisible: true },
    { key: 'stopLoss', label: 'SL', defaultVisible: true },
  ];

  visibleColumns = signal<string[]>([]);

  readonly visibleColumnSet = computed(() => new Set(this.visibleColumns()));

  private timerId: any;
  private subscription?: Subscription;

  // ======================================================
  // Computed
  // ======================================================

  publishedQuotes = computed(() => this.quotes());

  isWaitingForQuotes = computed(() => this.quotes().length === 0);

  filteredQuotes = computed(() => {
    const search = this.searchText().trim().toLowerCase();

    const filtered = this.publishedQuotes().filter((stock) =>
      stock.symbol.toLowerCase().includes(search),
    );

    return filtered.sort((left, right) => {
      const leftBucket = this.getQuoteSortBucket(left);
      const rightBucket = this.getQuoteSortBucket(right);

      if (leftBucket !== rightBucket) {
        return leftBucket - rightBucket;
      }

      return left.symbol.localeCompare(right.symbol);
    });
  });

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

    this.subscribeToQuotes();
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
    const configuredColumns = configuration?.['visibleColumns']?.filter(
      (columnKey: string) =>
        this.columnDefinitions.some((column) => column.key === columnKey),
    );

    const isLegacyColumnSet = configuredColumns?.length === 3
      && ['star', 'symbol', 'stopLoss'].every((key) => configuredColumns.includes(key));

    const nextColumns = configuredColumns?.length && !isLegacyColumnSet
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

  private async subscribeToQuotes(): Promise<void> {
    // Subscribe to the quote stream before connecting. MarketHub sends the
    // latest published quote snapshot as soon as this client connects.
    this.subscription?.unsubscribe();
    this.subscription = this.#market.quotes$.subscribe((quotes: any[]) => {
      this.quotes.set(quotes.map((quote) => this.toQuoteRow(quote)));
    });

    await this.#market.startConnection();
  }

  private toQuoteRow(quote: any): Gainer {
    const quantity = Number(quote.quantity ?? 0);
    const ltp = Number(quote.ltp ?? 0);
    const investedAmount = Number(quote.totalInvestment ?? 0);
    const averagePrice = quantity > 0 && investedAmount > 0
      ? investedAmount / quantity
      : 0;
    const currentValue = quantity * ltp;
    const isOwned = quantity > 0;

    return {
      symbol: String(quote.tradingSymbol ?? ''),
      symbolToken: String(quote.symbolToken ?? ''),
      exchange: String(quote.exchange ?? 'NSE'),
      productType: String(quote.productType ?? 'INTRADAY'),
      currentPrice: ltp,
      previousClose: Number(quote.previousClose ?? 0),
      changePercent: Number(quote.percentChange ?? 0),
      atr: 0,
      recoveryScore: 0,
      breakoutStrength: 0,
      signal: isOwned ? 'SELL' : 'HOLD',
      risk: '',
      reason: '',
      suggestion: '',
      isSubscribed: isOwned,
      stopLoss: Number(quote.stopLoss ?? 0),
      bid: Number(quote.bid ?? 0),
      ask: Number(quote.ask ?? 0),
      tickDayVolume: Number(quote.dayVolume ?? 0),
      isOwned,
      quantity,
      averagePrice,
      investedAmount,
      currentValue,
      profitLoss: currentValue - investedAmount,
      profitLossPercentage: investedAmount > 0
        ? ((currentValue - investedAmount) / investedAmount) * 100
        : 0,
      upperCircuitLimit: 0,
      lowerCircuitLimit: 0,
      quote,
    };
  }

  // ======================================================
  // Refresh
  // ======================================================

  refresh(): void {
    // Close the left controls panel first, then refresh the dashboard data.
    this.showSettings.set(false);

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


  private getQuoteSortBucket(stock: Gainer): number {
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
