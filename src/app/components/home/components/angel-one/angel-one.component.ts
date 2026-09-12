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
import { TradingConfiguration } from '../../models/trading-configuration';
import { Router } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TooltipDirective } from '../../../../directives/tooltip.directive';
import { SimulationService } from '../../services/simulation.service';

@Component({
  selector: 'app-angel-one',

  standalone: true,

  imports: [CommonModule, TooltipDirective],

  templateUrl: './angel-one.component.html',

  styleUrls: ['./angel-one.component.css'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AngelOneComponent implements OnInit, AfterViewInit, OnDestroy {
  readonly #angel = inject(AngelOneService);

  readonly #market = inject(MarketService);

  readonly #toast = inject(ToastService);
  readonly #simulation = inject(SimulationService);

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

  showSettings = signal(false);

  showPortfolio = signal(false);

  showLogs = signal(false);

  // Dashboard simulations run in-place with a fixed pool of up to five stocks.
  readonly simulationConcurrency = 5;
  simulationRunning = signal<Set<string>>(new Set());
  simulationCursors = signal<Record<string, number>>({});
  simulationExplanations = signal<Record<string, string>>({});
  simulationResults = signal<Record<string, any>>({});
  private simulationTimers = new Map<string, number>();
  private simulationQueue: Gainer[] = [];

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
    { key: 'simulation', label: 'Simulation', defaultVisible: true },
    { key: 'explanation', label: 'Explanation', defaultVisible: true },
    { key: 'aiExport', label: 'AI', defaultVisible: true },
    { key: 'star', label: '⭐', defaultVisible: true },
    { key: 'symbol', label: 'Symbol', defaultVisible: true },
    { key: 'instrumentType', label: 'Type', defaultVisible: true },
    { key: 'exchange', label: 'Exchange', defaultVisible: false },
    { key: 'optionContract', label: 'Option Contract', defaultVisible: false },
    { key: 'oi', label: 'OI', defaultVisible: false },
    { key: 'oiChange', label: 'OI Change %', defaultVisible: false },
    { key: 'pcr', label: 'PCR', defaultVisible: false },
    { key: 'iv', label: 'IV', defaultVisible: false },
    { key: 'delta', label: 'Delta', defaultVisible: false },
    { key: 'gamma', label: 'Gamma', defaultVisible: false },
    { key: 'theta', label: 'Theta', defaultVisible: false },
    { key: 'vega', label: 'Vega', defaultVisible: false },
    { key: 'token', label: 'Token', defaultVisible: false },
    { key: 'prevClose', label: 'Prev Close', defaultVisible: true },
    { key: 'vwap', label: 'VWAP', defaultVisible: false },
    { key: 'ema9', label: 'EMA9', defaultVisible: false },
    { key: 'ema21', label: 'EMA21', defaultVisible: false },
    { key: 'ema50', label: 'EMA50', defaultVisible: false },
    { key: 'ema200', label: 'EMA200', defaultVisible: false },
    { key: 'anchoredVWAP', label: 'Anchored VWAP', defaultVisible: false },
    { key: 'adx', label: 'ADX', defaultVisible: false },
    { key: 'superTrend', label: 'SuperTrend', defaultVisible: false },
    { key: 'superTrendBullish', label: 'ST Bullish', defaultVisible: false },
    { key: 'rsi', label: 'RSI', defaultVisible: false },
    { key: 'volumeMultiplier', label: 'Vol×', defaultVisible: false },
    { key: 'pullbackDistance', label: 'PB%', defaultVisible: false },
    { key: 'distanceFromEMA', label: 'Dist EMA%', defaultVisible: false },
    { key: 'distanceFromVWAP', label: 'Dist VWAP%', defaultVisible: false },
    { key: 'macd', label: 'MACD', defaultVisible: false },
    { key: 'macdSignal', label: 'MACD Sig', defaultVisible: false },
    { key: 'macdHistogram', label: 'MACD Hist', defaultVisible: false },
    {
      key: 'bollingerBandwidth',
      label: 'Boll Bandwidth',
      defaultVisible: false,
    },
    { key: 'score', label: 'Score', defaultVisible: true },
    { key: 'signal', label: 'Signal', defaultVisible: true },
    { key: 'risk', label: 'Risk', defaultVisible: true },
    { key: 'stopLoss', label: 'SL', defaultVisible: true },
    { key: 'targetPrice', label: 'Target', defaultVisible: true },
    { key: 'atr', label: 'ATR', defaultVisible: false },
    { key: 'reason', label: 'Reason', defaultVisible: true },
    { key: 'suggestion', label: 'Suggestion', defaultVisible: true },
    {
      key: 'upperCircuitLimit',
      label: 'Upper Circuit Limit',
      defaultVisible: false,
    },
    {
      key: 'lowerCircuitLimit',
      label: 'Lower Circuit Limit',
      defaultVisible: false,
    },
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

  filteredGainers = computed(() => {
    const search = this.searchText().trim().toLowerCase();

    const filtered = this.subscribedGainers().filter((stock) =>
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

  activeInstrumentType = computed(
    () => this.configuration()?.instrumentType ?? 'Equity',
  );

  activeInstrumentSettings = computed(() => {
    const config = this.configuration();
    if (!config) return undefined;
    switch (config.instrumentType) {
      case 'Futures':
        return config.futures;
      case 'Options':
        return config.options;
      default:
        return config.equity;
    }
  });

  riskPercentage = computed(
    () => this.activeInstrumentSettings()?.riskPercentage ?? 0,
  );

  maxDailyTrades = computed(() => {
    const settings = this.activeInstrumentSettings() as any;
    return settings?.maximumDailyTrades ?? 0;
  });

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
    this.simulationTimers.forEach((timer) => window.clearTimeout(timer));
    this.simulationTimers.clear();
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

  openSimulation(): void {
    void this.#router.navigate(['/home/simulation']);
  }

  isSimulationAvailable(stock: Gainer): boolean {
    return stock.isSubscribed === true && stock.historicalLoaded === true;
  }

  openSimulationForStock(stock: Gainer): void {
    if (!this.isSimulationAvailable(stock)) {
      this.#toast.error('Simulation is available only when indicators are loaded.');
      return;
    }
    const key = this.simulationKey(stock);
    if (this.simulationRunning().has(key)) {
      this.cancelStockSimulation(stock);
      return;
    }
    this.simulationQueue.push(stock);
    this.pumpSimulationQueue();
  }

  simulationKey(stock: Gainer): string {
    return String(stock.symbolToken ?? stock.symbol);
  }

  isStockSimulationRunning(stock: Gainer): boolean {
    return this.simulationRunning().has(this.simulationKey(stock));
  }

  stockSimulationExplanation(stock: Gainer): string {
    return this.simulationExplanations()[this.simulationKey(stock)] || 'Not simulated yet.';
  }

  private pumpSimulationQueue(): void {
    while (this.simulationQueue.length && this.simulationRunning().size < this.simulationConcurrency) {
      const stock = this.simulationQueue.shift()!;
      if (!this.isSimulationAvailable(stock) || this.isStockSimulationRunning(stock)) continue;
      void this.startStockSimulation(stock);
    }
  }

  private async startStockSimulation(stock: Gainer): Promise<void> {
    const key = this.simulationKey(stock);
    this.simulationRunning.update((set) => new Set(set).add(key));
    this.simulationExplanations.update((x) => ({ ...x, [key]: 'Loading captured market data…' }));
    try {
      // Grid simulations are independent per stock. Do not activate the application's
      // global HTTP loader, otherwise one running stock blocks the other simulation rows.
      const response = await firstValueFrom(
        this.#simulation.getLive(stock.symbolToken || stock.symbol, true),
      );
      if (!this.simulationRunning().has(key)) return;
      const capture: any = response?.capture;
      const ticks = (capture?.candles ?? []).flatMap((c: any) => c.ticks ?? []).filter((t: any) => Number(t.ltp) > 0).sort((a: any,b: any) => this.gridTime(a)-this.gridTime(b));
      if (!ticks.length) {
        this.simulationExplanations.update((x) => ({ ...x, [key]: 'Simulation could not start because no valid ticks were captured.' }));
        return;
      }
      this.simulationCursors.update((x) => ({ ...x, [key]: 0 }));
      this.simulationExplanations.update((x) => ({ ...x, [key]: this.explainGridTick(ticks[0], stock) }));
      await new Promise<void>((resolve) => {
        const step = (i: number) => {
          if (!this.simulationRunning().has(key)) { resolve(); return; }
          if (i >= ticks.length) {
            const result = this.buildGridSimulationResult(stock, ticks);
            this.simulationResults.update((x) => ({ ...x, [key]: { ...result, capture } }));
            this.simulationExplanations.update((x) => ({ ...x, [key]: result.explanation }));
            resolve(); return;
          }
          const tick = ticks[i];
          this.simulationCursors.update((x) => ({ ...x, [key]: i + 1 }));
          this.simulationExplanations.update((x) => ({ ...x, [key]: this.explainGridTick(tick, stock) }));
          this.simulationTimers.set(key, window.setTimeout(() => step(i + 1), 80));
        };
        step(0);
      });
    } catch (e) {
      this.simulationExplanations.update((x) => ({ ...x, [key]: e instanceof Error ? `Simulation failed: ${e.message}` : 'Simulation failed.' }));
    } finally {
      const timer = this.simulationTimers.get(key);
      if (timer) window.clearTimeout(timer);
      this.simulationTimers.delete(key);
      this.simulationRunning.update((set) => { const next = new Set(set); next.delete(key); return next; });
      this.pumpSimulationQueue();
    }
  }

  cancelStockSimulation(stock: Gainer): void {
    const key = this.simulationKey(stock);
    const timer = this.simulationTimers.get(key);
    if (timer) window.clearTimeout(timer);
    this.simulationTimers.delete(key);
    this.simulationQueue = this.simulationQueue.filter(x => this.simulationKey(x) !== key);
    this.simulationRunning.update((set) => { const next = new Set(set); next.delete(key); return next; });
    this.simulationExplanations.update((x) => ({ ...x, [key]: 'Simulation cancelled.' }));
    this.pumpSimulationQueue();
  }

  private gridTime(t: any): number { return new Date(t?.exchangeTime || t?.utc || 0).getTime(); }

  private explainGridTick(tick: any, stock: Gainer): string {
    const decision = String(tick?.decision || stock.signal || 'HOLD').toUpperCase();
    const reason = String(tick?.decisionReason || stock.reason || stock.suggestion || '').trim();
    const stage = String(tick?.stage || tick?.status || 'OBSERVING').toUpperCase();
    const price = Number(tick?.ltp || stock.currentPrice || 0);
    return `${stage}: ${decision} at ₹${price.toFixed(2)}${reason ? ` — ${reason}` : ' — replaying the captured decision inputs.'}`;
  }

  private buildGridSimulationResult(stock: Gainer, ticks: any[]): any {
    const entries = ticks.filter(t => String(t.stage || '').toUpperCase() === 'ENTRY' || String(t.status || '').toUpperCase() === 'ENTRY');
    const exits = ticks.filter(t => String(t.stage || '').toUpperCase() === 'EXIT' || String(t.status || '').toUpperCase() === 'EXIT');
    const rejected = ticks.filter(t => ['REJECTED','EXPIRED'].includes(String(t.stage || t.status || '').toUpperCase()));
    const entry = entries[0]?.ltp;
    const exit = exits.at(-1)?.ltp;
    const pnl = Number.isFinite(Number(entry)) && Number.isFinite(Number(exit)) ? Number(exit) - Number(entry) : 0;
    const last = ticks.at(-1);
    const explanation = `${entries.length ? `Captured ${entries.length} entry event(s)` : 'No accepted entry was captured'}; ${exits.length ? `${exits.length} exit event(s)` : 'no exit event'}; ${rejected.length} blocked/expired observation(s). ${this.explainGridTick(last, stock)}`;
    return { symbol: stock.symbol, trades: entries.length, exits: exits.length, rejected: rejected.length, entryPrice: entry ?? 0, exitPrice: exit ?? 0, netProfit: pnl, explanation, generatedAtIST: new Date().toLocaleString('en-IN', { timeZone: 'Asia/Kolkata' }) };
  }

  exportStockSimulationForAi(stock: Gainer): void {
    const key = this.simulationKey(stock);
    const result = this.simulationResults()[key];
    if (!result) { this.#toast.error('Run the stock simulation before exporting it for AI.'); return; }
    const payload = { schemaVersion: '2.0', exportType: 'trading-simulation-ai-context', timezone: 'Asia/Kolkata', stock: stock.symbol, simulation: result, capturedContext: result.capture, aiInstructions: ['Analyze this stock chronologically.', 'Identify every missed entry, avoidable loss and exit problem supported by the data.', 'Explain the evidence and propose concrete threshold/logic changes.', 'Do not invent unavailable market data.'] };
    this.downloadJson(payload, `${stock.symbol}-simulation-ai-context-${this.fileStamp()}.json`);
  }

  private downloadJson(payload: unknown, fileName: string): void {
    const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob); const a = document.createElement('a'); a.href = url; a.download = fileName; a.click(); URL.revokeObjectURL(url);
  }

  private fileStamp(): string { return new Date().toISOString().replace(/[^0-9]/g, '').slice(0, 14); }

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
    const configuredColumns = configuration?.visibleColumns?.filter(
      (columnKey) =>
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
    // Subscribe BEFORE starting SignalR. MarketHub sends the current watchlist
    // from OnConnectedAsync; subscribing afterwards can miss that one-shot
    // snapshot and leave the dashboard empty until the next market update.
    this.subscription?.unsubscribe();
    this.subscription = this.#market.gainers$.subscribe((data: Gainer[]) => {
      // SignalR can deliver a stale SELL after the authoritative snapshot.
      // The UI must never present SELL for an unowned equity, even if an old
      // event arrives after the corrected API snapshot. Owned equities retain
      // SELL because they may legitimately need an exit.
      const normalized = data.map((stock) => this.normalizeGainerForDisplay(stock));
      this.gainers.set(normalized);
    });

    await this.#market.startConnection();
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

  private normalizeGainerForDisplay(stock: Gainer): Gainer {
    const instrumentType = String(stock.instrumentType ?? 'Equity')
      .trim()
      .toLowerCase();
    const isEquity = instrumentType === '' || instrumentType === 'equity';
    const signal = String(stock.signal ?? '').trim().toUpperCase();

    if (isEquity && !stock.isOwned && signal === 'SELL') {
      return { ...stock, signal: 'HOLD' };
    }

    return stock;
  }

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
