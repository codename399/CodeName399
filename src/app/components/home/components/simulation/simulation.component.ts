import { CommonModule } from '@angular/common';
import { Component, computed, signal, inject, OnDestroy, OnInit } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
import { MarketService } from '../../services/market.service';
import { Gainer } from '../../models/gainer';
import { SimulationService } from '../../services/simulation.service';
import { FormsModule } from '@angular/forms';
import * as XLSX from 'xlsx';
import {
  CandleCapture,
  ConfigurationRecommendation,
  DiagnosticIssue,
  FuturePathAnalysis,
  GlobalConfigurationResult,
  GlobalLearningResult,
  ConfigurationProposal,
  Indicators,
  SimulationResult,
  StockCapture,
  Tick,
  GateEvaluation,
  ReplayDecision,
  ActualTrade,
  ExitReplay,
  DecisionDebugStep,
  RegimeLearningResult,
  RegimeConfiguration,
} from './models';

@Component({
  selector: 'app-trading-simulation',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './simulation.component.html',
  styleUrl: './simulation.component.css',
})
export class TradingSimulationComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly marketService = inject(MarketService);
  private readonly simulationService = inject(SimulationService);
  private marketSubscriptions = new Subscription();
  private liveTickCounts = new Map<string, number>();

  ngOnInit(): void {
    this.marketSubscriptions.add(this.marketService.gainers$.subscribe(data => this.updateLiveStocks(data)));
    this.marketSubscriptions.add(this.marketService.ticks$.subscribe(stock => this.handleLiveTick(stock)));
    void this.marketService.startConnection();
    this.marketSubscriptions.add(this.route.queryParamMap.subscribe(params => {
      const symbol = params.get('symbol');
      const mode = params.get('source');
      const autoRun = params.get('autoRun') === '1';
      if (mode === 'live' || symbol) {
        this.sourceMode.set('live');
        if (symbol) void this.loadLiveStock(symbol, autoRun);
      }
    }));
  }

  ngOnDestroy(): void { this.marketSubscriptions.unsubscribe(); }

  returnToDashboard(): void {
    void this.router.navigate(['/home/angel-one']);
  }

  stocks = signal<StockCapture[]>([]);
  sourceMode = signal<'excel'|'live'|'saved'>('excel');
  liveStocks = signal<any[]>([]);
  liveSearch = signal('');
  liveSignalFilter = signal('ALL');
  liveOwnedFilter = signal<'ALL'|'OWNED'|'NOT_OWNED'>('ALL');
  liveMinTicks = signal(0);
  liveLoading = signal(false);
  selectedLiveStock = signal<any | null>(null);
  savedSimulations = signal<any[]>([]);
  savedLoading = signal(false);
  selectedSavedId = signal('');
  saveName = signal('');
  liveDataRefreshing = signal(false);
  entryExplanation = signal('');
  selectedSymbol = signal('');
  selectedCandle = signal(0);
  playing = signal(false);
  playIndex = signal(0);
  replayTick = signal<Tick | null>(null);
  replayStatus = signal('Ready');
  replaySpeedMs = signal(80);
  replayCursor = signal(0);
  tickPage = signal(0);
  readonly tickPageSize = 100;

  private chartCacheKey = '';
  private chartCache = new Map<string, string>();
  private chartMetaCache: { key: string; min: number; max: number; ticks: Tick[]; labels: Array<{ x: number; text: string }>; markers: Array<{ x: number; y: number; stage: string }> } | null = null;
  simulationCompleted = signal(false);
  actionBusy = signal(false);
  busyAction = signal('');
  actionStatus = signal('');
  activeTab = signal<'overview' | 'ticks' | 'decisions' | 'simulation'>(
    'overview',
  );
  error = signal('');
  evaluatedObservationCount = signal(0);
  skippedObservationCount = signal(0);
  toggles: Record<string, boolean> = {
    EMA9: true,
    EMA21: true,
    EMA50: true,
    EMA200: false,
    VWAP: true,
    AnchoredVWAP: false,
    SuperTrend: false,
    Bollinger: true,
    RSI: true,
    MACD: false,
    ADX: false,
  };
  params = {
    minimumScore: 70,
    minimumConfidence: 65,
    minimumRiskReward: 1.5,
    minimumExpectedNetValue: 0,
    minimumEdgeScore: 45,
    minimumProfitPercent: 0.3,
    maximumSpreadPercent: 1.5,
    useAdaptiveScore: true,
  };
  simulation = signal<SimulationResult>({
    netProfit: 0,
    trades: 0,
    wins: 0,
    losses: 0,
    missed: 0,
    actualLosses: 0,
    avoidableLosses: 0,
    missedProfit: 0,
    bestEntry: 0,
    bestExit: 0,
    bestEntryTime: '',
    bestExitTime: '',
    opportunityPercent: 0,
    reasons: [],
    recommendation: 'Upload analysis files to begin.',
    issues: [],
    recommendations: [],
  });
  globalOptimization = signal<GlobalConfigurationResult | null>(null);
  globalLearning = signal<GlobalLearningResult | null>(null);
  regimeLearning = signal<RegimeLearningResult | null>(null);
  configurationProposal = signal<ConfigurationProposal | null>(null);
  combinedSimulation = signal<any | null>(null);
  filteredLiveStocks = computed(() => {
    const search = this.liveSearch().trim().toLowerCase();
    const signalFilter = this.liveSignalFilter();
    const ownedFilter = this.liveOwnedFilter();
    const minTicks = this.liveMinTicks();
    return this.liveStocks().filter(s =>
      (!search || String(s.symbol ?? '').toLowerCase().includes(search)) &&
      (signalFilter === 'ALL' || String(s.signal ?? '').toUpperCase() === signalFilter) &&
      (ownedFilter === 'ALL' || (ownedFilter === 'OWNED' ? !!s.isOwned : !s.isOwned)) &&
      (Number(s.tickCount ?? 0) >= minTicks)
    );
  });

  stock = computed<StockCapture | undefined>(
    () =>
      this.stocks().find((s) => s.symbol === this.selectedSymbol()) ??
      this.stocks()[0],
  );
  candle = computed<CandleCapture | undefined>(() => {
    const stock = this.stock();
    if (!stock?.candles?.length) return undefined;
    return stock.candles[this.selectedCandle()] ?? stock.candles[0];
  });
  candles = computed<CandleCapture[]>(() => this.stock()?.candles ?? []);
  ticks = computed(() => this.stock()?.candles.flatMap((c) => c.ticks) ?? []);
  validTicks = computed(() => this.ticks().filter((t) => t.ltp > 0).sort((a, b) => this.time(a) - this.time(b)));
  latestTick = computed<Tick | null>(() => { const ticks = this.ticks().filter(x => x.ltp > 0); return ticks.length ? ticks[ticks.length - 1] : null; });
  livePrice = computed(() => this.replayTick()?.ltp || this.selectedLiveStock()?.currentPrice || this.latestTick()?.ltp || 0);
  pagedTicks = computed(() => {
    const all = this.validTicks();
    const start = this.tickPage() * this.tickPageSize;
    return all.slice(start, start + this.tickPageSize);
  });
  tickPageCount = computed(() => Math.max(1, Math.ceil(this.validTicks().length / this.tickPageSize)));
  indicators = computed(() => this.candle()?.indicators ?? {});
  decision = computed(() => this.candle()?.decision ?? {});
  decisionDisplay = computed(() => {
    const c = this.candle();
    const d = c?.decision ?? {};
    const t = c?.ticks?.[c.ticks.length - 1];
    const rp = this.simulation().replay;
    const indicator = (name: string): unknown =>
      this.indicatorLookup(c?.indicators, name) ?? this.indicatorLookup(t?.indicators, name);
    const value = (...values: unknown[]): unknown =>
      values.find(v => v !== undefined && v !== null && String(v).trim() !== '');
    const numeric = (...values: unknown[]): number => {
      for (const v of values) {
        const n = Number(v);
        if (v !== undefined && v !== null && String(v).trim() !== '' && Number.isFinite(n)) return n;
      }
      return 0;
    };
    const failures = rp?.gates?.filter(g => g.status === 'FAIL').map(g => g.name).join(' | ') || '';
    const policyScore = rp?.appliedPolicy?.minimumScore;
    const signal = String(value(d['signal'], t?.decision, indicator('Signal'), rp?.signal, 'HOLD') ?? 'HOLD').toUpperCase();
    const score = numeric(d['score'], indicator('Score'));

    const confidence = numeric(d['adaptiveConfidence'], t?.confidence, indicator('Confidence'));
    const riskReward = numeric(d['adaptiveRiskReward'], t?.adaptiveRiskReward, indicator('AdaptiveRiskReward'));
    const expectedValue = numeric(d['adaptiveExpectedNetValue'], t?.adaptiveExpectedNetValue, indicator('AdaptiveExpectedNetValue'));
    const edge = numeric(d['adaptiveEdgeScore'], t?.adaptiveEdgeScore, indicator('AdaptiveEdgeScore'));
    const reason = String(value(d['reason'], t?.decisionReason, rp?.firstBlockingGate ? `Entry blocked by ${rp.firstBlockingGate}.` : '', signal === 'BUY' ? 'Captured BUY setup replayed at this observation.' : 'No captured entry decision at this observation.') ?? 'No decision captured');
    return {
      signal,
      reason,
      score,
      adaptiveRequiredEntryScore: numeric(d['adaptiveRequiredEntryScore'], policyScore, this.params.minimumScore),
      adaptiveConfidence: confidence,
      adaptiveRiskReward: riskReward,
      adaptiveExpectedNetValue: expectedValue,
      adaptiveEdgeScore: edge,
      gateFailures: failures || String(d['gateFailures'] ?? ''),
    };
  });
  currentConfiguration = computed(() => this.stock()?.configuration ?? {});
  configurationEntries = computed(() =>
    Object.entries(this.currentConfiguration())
      .filter(([k]) => !!k)
      .sort(([a], [b]) => a.localeCompare(b)),
  );
  configurationCount = computed(() => this.configurationEntries().length);
  configurationCapturedAt = computed(
    () => this.stock()?.configurationCapturedAt ?? '',
  );
  configurationVersion = computed(
    () =>
      this.configValue(this.currentConfiguration(), 'configurationVersion') ??
      '',
  );

  async upload(files: FileList | null): Promise<void> {
    if (!files?.length) return;
    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('upload');
    this.actionStatus.set('Loading workbook(s)…');
    const loaded: StockCapture[] = [];
    for (const file of Array.from(files)) {
      try {
        loaded.push(await this.readWorkbook(file));
      } catch (e) {
        this.error.set(
          `${file.name}: ${e instanceof Error ? e.message : 'Unable to parse workbook'}`,
        );
      }
    }
    if (loaded.length) {
      this.stocks.set(loaded.sort((a, b) => a.symbol.localeCompare(b.symbol)));
      this.selectedSymbol.set(loaded[0].symbol);
      this.selectedCandle.set(this.firstDecisionCandleIndex(loaded[0]));
      this.replayCursor.set(0);
      this.tickPage.set(0);
      this.invalidateChartCache();
      this.simulationCompleted.set(true);
      this.loadParametersFromConfiguration(loaded[0].configuration);
      this.runSimulation();
      this.scrollSelectedStockIntoView();
      this.actionStatus.set(`Loaded ${loaded.length} workbook${loaded.length === 1 ? '' : 's'}.`);
    } else {
      this.actionStatus.set('Unable to load the selected workbook(s).');
    }
    this.actionBusy.set(false);
    this.busyAction.set('');
  }
  private scrollSelectedStockIntoView(): void {
    setTimeout(() => {
      const selected = document.querySelector('.stock-list button.active') as HTMLElement | null;
      selected?.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
    }, 0);
  }

  private updateLiveStocks(data: Gainer[] | any[]): void {
    const list = (data ?? []).filter((x: any) => x?.isSubscribed === true && x?.historicalLoaded === true).map((x: any) => ({
      symbol: String(x.symbol ?? ''), token: String(x.symbolToken ?? ''), exchange: String(x.exchange ?? 'NSE'), signal: String(x.signal ?? ''),
      isOwned: !!x.isOwned, indicatorsLoaded: !!x.historicalLoaded, currentPrice: Number(x.currentPrice ?? x.tickLtp ?? 0), score: Number(x.score ?? 0),
      adaptiveConfidence: Number(x.adaptiveConfidence ?? 0), adaptiveEdgeScore: Number(x.adaptiveEdgeScore ?? 0), adaptiveRiskReward: Number(x.adaptiveRiskReward ?? 0),
      tickCount: this.liveTickCounts.get(String(x.symbolToken ?? '')) ?? 0, lastTickUpdatedUtc: x.lastTickUpdated ?? ''
    }));
    this.liveStocks.set(list.sort((a,b) => (Number(b.isOwned)-Number(a.isOwned)) || (Number(b.score)-Number(a.score)) || a.symbol.localeCompare(b.symbol)));
    const selected = this.selectedLiveStock();
    if (selected) { const refreshed = list.find(x => x.token === selected.token || x.symbol === selected.symbol); if (refreshed) this.selectedLiveStock.set(refreshed); }
  }

  private handleLiveTick(stock: any): void {
    const token = String(stock?.symbolToken ?? ''); if (!token) return;
    this.liveTickCounts.set(token, (this.liveTickCounts.get(token) ?? 0) + 1);
    this.updateLiveStocks(this.marketService.getCurrentGainers());
    const selected = this.selectedLiveStock(); if (!selected || selected.token !== token) return;
    const tick = this.mapSignalRTick(stock, this.liveTickCounts.get(token) ?? 1);
    const current = this.stocks().find(s => s.token === token || s.symbol === stock.symbol); if (!current) return;
    const candles = [...current.candles]; const minute = new Date(this.time(tick)); minute.setSeconds(0, 0); const key = minute.toISOString();
    let ci = candles.findIndex(c => c.timestamp === key || c.candle.timestamp === key);
    if (ci < 0) {
      candles.push({ index: candles.length, timestamp: key, candle: { timestamp: key, open: tick.ltp, high: tick.ltp, low: tick.ltp, close: tick.ltp, volume: tick.dayVolume || 0 }, indicators: tick.indicators ?? {}, decision: { signal: stock.signal ?? '', score: stock.score ?? 0, adaptiveConfidence: stock.adaptiveConfidence ?? 0, adaptiveRiskReward: stock.adaptiveRiskReward ?? 0, adaptiveEdgeScore: stock.adaptiveEdgeScore ?? 0, reason: stock.reason ?? '', suggestion: stock.suggestion ?? '' }, virtualTrade: {}, actualTrade: {}, ticks: [tick], loadedDecisionFields: ['signal','score','adaptiveConfidence','adaptiveRiskReward','adaptiveEdgeScore'] });
      ci = candles.length - 1;
    } else {
      const c = candles[ci];
      candles[ci] = { ...c, candle: { ...c.candle, high: Math.max(c.candle.high, tick.ltp), low: Math.min(c.candle.low, tick.ltp), close: tick.ltp, volume: tick.dayVolume || c.candle.volume }, indicators: tick.indicators ?? c.indicators, decision: { ...c.decision, signal: stock.signal ?? c.decision['signal'], score: stock.score ?? c.decision['score'], adaptiveConfidence: stock.adaptiveConfidence ?? c.decision['adaptiveConfidence'], adaptiveRiskReward: stock.adaptiveRiskReward ?? c.decision['adaptiveRiskReward'], adaptiveEdgeScore: stock.adaptiveEdgeScore ?? c.decision['adaptiveEdgeScore'], reason: stock.reason ?? c.decision['reason'], suggestion: stock.suggestion ?? c.decision['suggestion'] }, ticks: [...c.ticks, tick] };
    }
    this.stocks.set([{ ...current, candles }]); this.selectedCandle.set(ci); this.replayTick.set(tick);
  }

  private mapSignalRTick(stock: any, n: number): Tick {
    const ts = stock.tickExchangeTime || stock.lastTickUpdated || new Date().toISOString();
    return { n, utc: new Date(ts).toISOString(), exchangeTime: new Date(ts).toISOString(), sequence: Number(stock.tickSequenceNumber ?? n), ltp: Number(stock.tickLtp ?? stock.currentPrice ?? 0), bid: Number(stock.tickBid ?? stock.bid ?? 0), ask: Number(stock.tickAsk ?? stock.ask ?? 0), spread: Number(stock.spread ?? 0), spreadPct: Number(stock.spreadPercent ?? 0), open: Number(stock.tickOpen ?? 0), high: Number(stock.tickHigh ?? 0), low: Number(stock.tickLow ?? 0), close: Number(stock.tickClose ?? 0), ltq: Number(stock.tickLastTradedQuantity ?? 0), avgPrice: Number(stock.tickAveragePrice ?? 0), dayVolume: Number(stock.tickDayVolume ?? 0), buyQty: Number(stock.tickTotalBuyQuantity ?? 0), sellQty: Number(stock.tickTotalSellQuantity ?? 0), decision: stock.signal ?? '', decisionReason: stock.reason ?? '', indicators: { EMA9: Number(stock.ema9 ?? 0), EMA21: Number(stock.ema21 ?? 0), EMA50: Number(stock.ema50 ?? 0), EMA200: Number(stock.ema200 ?? 0), VWAP: Number(stock.vwap ?? 0), AnchoredVWAP: Number(stock.anchoredVWAP ?? 0), RSI: Number(stock.rsi ?? 0), MACD: Number(stock.macd ?? 0), MACDSignal: Number(stock.macdSignal ?? 0), MACDHistogram: Number(stock.macdHistogram ?? 0), ATR: Number(stock.atr ?? 0), ADX: Number(stock.adx ?? 0), SuperTrend: Number(stock.superTrend ?? 0), BollingerUpper: Number(stock.bollingerUpper ?? 0), BollingerMiddle: Number(stock.bollingerMiddle ?? 0), BollingerLower: Number(stock.bollingerLower ?? 0), SpreadPercent: Number(stock.spreadPercent ?? 0), Score: Number(stock.score ?? 0), Signal: String(stock.signal ?? '') } };
  }

  async loadLiveStocks(): Promise<void> { this.sourceMode.set('live'); this.liveLoading.set(true); try { this.updateLiveStocks(this.marketService.getCurrentGainers()); this.scrollSelectedStockIntoView(); } finally { this.liveLoading.set(false); } }
  async refreshLiveStocks(): Promise<void> { await this.loadLiveStocks(); }
  async loadLiveStock(symbolOrToken: string, autoRun = false): Promise<void> {
    if (!symbolOrToken) return; this.sourceMode.set('live'); this.liveDataRefreshing.set(true); this.error.set('');
    try {
      const response = await new Promise<any>((resolve, reject) => this.simulationService.getLive(symbolOrToken).subscribe({ next: resolve, error: reject }));
      if (!response?.capture) throw new Error('No simulation data is currently available for this stock.');
      this.stocks.set([response.capture]); this.selectedSymbol.set(response.capture.symbol); this.selectedCandle.set(this.firstDecisionCandleIndex(response.capture)); this.playIndex.set(0); this.replayTick.set(null);
      const summary = this.liveStocks().find(x => x.symbol === response.capture.symbol || x.token === response.capture.token) ?? null; this.selectedLiveStock.set(summary);
      this.loadParametersFromConfiguration(response.capture.configuration); this.runSimulation(); this.scrollSelectedStockIntoView();
      if (autoRun) { await this.yieldToUi(); document.querySelector('.chart')?.scrollIntoView({ behavior: 'smooth', block: 'center' }); await this.runLive(); }
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Unable to load live simulation data.'); } finally { this.liveDataRefreshing.set(false); }
  }
  refreshSelectedLive(): void { const selected = this.selectedLiveStock(); if (selected) void this.loadLiveStock(selected.symbol, false); }
  setLiveMinTicks(value: number): void { const n = Number(value); this.liveMinTicks.set(Number.isFinite(n) ? Math.max(0, Math.floor(n)) : 0); }
  selectLiveStock(symbol: string): void { void this.loadLiveStock(symbol, false); }

  loadSavedList(): void {
    this.savedLoading.set(true); this.simulationService.listSaved(100).subscribe({ next: list => this.savedSimulations.set(list ?? []), error: e => this.error.set(e instanceof Error ? e.message : 'Unable to load saved simulations.'), complete: () => this.savedLoading.set(false) });
  }
  loadSavedSimulation(id: string): void {
    if (!id) return; this.savedLoading.set(true);
    this.simulationService.loadSaved(id).subscribe({ next: capture => { this.sourceMode.set('saved'); this.stocks.set([capture]); this.selectedSavedId.set(id); this.selectedSymbol.set(capture.symbol); this.selectedCandle.set(this.firstDecisionCandleIndex(capture)); this.playIndex.set(0); this.replayTick.set(null); this.loadParametersFromConfiguration(capture.configuration); this.runSimulation(); this.scrollSelectedStockIntoView(); }, error: e => this.error.set(e instanceof Error ? e.message : 'Unable to load saved simulation.'), complete: () => this.savedLoading.set(false) });
  }
  async simulateAllFromUi(): Promise<void> {
    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('combined-simulation');
    this.actionStatus.set('Preparing combined simulation…');
    try {
      let captures: StockCapture[] = [];
      if (this.sourceMode() === 'excel') {
        captures = [...this.stocks()];
      } else if (this.sourceMode() === 'live') {
        const selected = this.filteredLiveStocks().length ? this.filteredLiveStocks() : this.liveStocks();
        const responses = await Promise.all(selected.map(s => firstValueFrom(this.simulationService.getLive(String(s.symbolToken || s.symbol)))));
        captures = responses.map(r => r.capture).filter(Boolean);
      } else {
        const list = this.savedSimulations();
        const loaded = await Promise.all(list.map(s => firstValueFrom(this.simulationService.loadSaved(String(s.id)))));
        captures = loaded.filter(Boolean);
      }
      if (!captures.length) {
        this.error.set('There are no simulations available for the selected source.');
        this.actionStatus.set('Combined simulation could not start.');
        return;
      }

      // Fast path: combined simulation is intentionally a computation-only pass.
      // Do not animate/replay each stock and do not yield between stocks; those UI
      // updates make large combined runs dramatically slower. Live/saved capture
      // loading is already parallelized above.
      const results: any[] = [];
      for (let i = 0; i < captures.length; i++) {
        const capture = captures[i];
        if (i === 0 || i === captures.length - 1 || i % 10 === 0) {
          this.actionStatus.set(`Simulating ${i + 1} of ${captures.length}: ${capture.symbol}…`);
        }
        const result = this.runSimulationForCapture(capture);
        results.push({
          symbol: capture.symbol,
          token: capture.token,
          exchange: capture.exchange,
          simulation: result,
          configuration: capture.configuration,
          dataQuality: {
            candleCount: capture.candles?.length ?? 0,
            tickCount: capture.candles?.reduce((n, c) => n + (c.ticks?.length ?? 0), 0) ?? 0,
            firstTimestamp: capture.candles?.[0]?.timestamp ?? '',
            lastTimestamp: capture.candles?.at(-1)?.timestamp ?? '',
          },
          // Keep the complete evidence needed to diagnose entry and exit behavior.
          // This is deliberately not reduced to summary metrics.
          candles: capture.candles,
        });
      }
      const totals = results.reduce((a, x) => {
        const r = x.simulation;
        a.netProfit += Number(r.netProfit || 0); a.trades += Number(r.trades || 0); a.wins += Number(r.wins || 0);
        a.losses += Number(r.losses || 0); a.missed += Number(r.missed || 0); a.missedProfit += Number(r.missedProfit || 0); a.avoidableLosses += Number(r.avoidableLosses || 0); return a;
      }, { netProfit: 0, trades: 0, wins: 0, losses: 0, missed: 0, missedProfit: 0, avoidableLosses: 0 });
      const combined = {
        schemaVersion: '2.0',
        exportType: 'combined-trading-simulation-ai-context',
        generatedAtIST: this.istTime(new Date().toISOString()),
        timezone: 'Asia/Kolkata',
        source: this.sourceMode().toUpperCase(),
        portfolioSummary: { stocks: results.length, ...totals },
        stocks: results,
        aiInstructions: [
          'Act as a trading-system diagnostic analyst, not a trade executor.',
          'Analyze every stock chronologically and then analyze the portfolio-wide pattern.',
          'Identify EVERY entry problem and EVERY exit problem: missed entries, blocked/rejected entries, late entries, losing entries, premature exits, late exits, stop/target failures, trailing-exit failures, and positions with no valid exit.',
          'For every problem, cite the exact stock, timestamp, price, signal, score/confidence/edge, relevant indicators, failed gate, actual trade state and supporting candle/tick evidence when available.',
          'Use the supplied candles and ticks as the source of truth. Reconstruct the chronological decision path instead of relying only on aggregate totals.',
          'Compare captured behavior with replay/counterfactual behavior and clearly separate what actually happened from what could have happened.',
          'Infer common root causes across stocks and separate them from stock-specific issues.',
          'Propose concrete changes to entry gates, exit logic, thresholds, risk/reward, spread handling, stop/target/trailing behavior and regime handling.',
          'For each proposed fix, state exactly which trades/problems it fixes, which profitable trades it might affect, the evidence supporting it, and the expected trade-off.',
          'Produce a prioritized implementation plan that fixes the maximum number of entry and exit problems while protecting profitable trades.',
          'Do not invent missing ticks, indicators, configuration values or market events. If evidence is missing, explicitly mark the conclusion as uncertain.',
          'The final answer must contain actionable code/configuration-level recommendations for the trading logic, grouped into entry fixes, exit fixes, risk fixes and validation/rollback steps.',
          'Distinguish original captured decisions from replay/counterfactual conclusions.',
        ],
      };
      this.combinedSimulation.set(combined);
      this.activeTab.set('simulation');
      this.simulationCompleted.set(true);
      this.actionStatus.set(`Combined simulation complete — ${results.length} stock(s), ${totals.trades} trade(s), ${totals.missed} missed opportunity(ies).`);
    } catch (e) {
      this.error.set(e instanceof Error ? e.message : 'Combined simulation failed.');
      this.actionStatus.set('Combined simulation failed.');
    } finally {
      this.actionBusy.set(false);
      this.busyAction.set('');
    }
  }

  private runSimulationForCapture(capture: StockCapture): SimulationResult {
    const previousStocks = this.stocks();
    const previousSymbol = this.selectedSymbol();
    this.stocks.set([capture]);
    this.selectedSymbol.set(capture.symbol);
    this.loadParametersFromConfiguration(capture.configuration);
    this.runSimulation();
    const result = structuredClone(this.simulation());
    this.stocks.set(previousStocks);
    this.selectedSymbol.set(previousSymbol);
    return result;
  }

  exportCombinedSimulationForAi(): void {
    const payload = this.combinedSimulation();
    if (!payload) {
      this.error.set('Run Simulate all before exporting the combined AI context.');
      return;
    }
    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('combined-ai-export');
    this.actionStatus.set('Preparing combined AI export…');
    setTimeout(() => {
      try {
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a'); a.href = url; a.download = `combined-simulation-ai-context-${this.fileStamp()}.json`; a.click(); URL.revokeObjectURL(url);
        this.actionStatus.set('Combined AI-ready simulation context exported.');
      } catch (e) {
        this.error.set(e instanceof Error ? e.message : 'Combined AI export failed.');
        this.actionStatus.set('Combined AI export failed.');
      } finally { this.actionBusy.set(false); this.busyAction.set(''); }
    }, 0);
  }

  saveCurrentSimulation(): void {
    const current = this.stock(); if (!current) return;
    this.actionBusy.set(true); this.busyAction.set('save'); this.actionStatus.set('Saving simulation…');
    this.simulationService.save({ name: this.saveName().trim() || `${current.symbol} simulation`, symbol: current.symbol, source: this.sourceMode().toUpperCase(), data: current }).subscribe({ next: () => { this.actionStatus.set('Simulation saved.'); this.saveName.set(''); }, error: e => { this.error.set(e instanceof Error ? e.message : 'Unable to save simulation.'); this.actionStatus.set('Save failed.'); }, complete: () => { this.actionBusy.set(false); this.busyAction.set(''); this.loadSavedList(); } });
  }
  deleteSavedSimulation(id: string, name: string): void {
    if (!id) return; if (!window.confirm(`Delete saved simulation “${name || id}”?\n\nThis action cannot be undone.`)) return;
    this.savedLoading.set(true); this.error.set('');
    this.simulationService.deleteSaved(id).subscribe({ next: () => { if (this.selectedSavedId() === id) { this.selectedSavedId.set(''); this.stocks.set([]); this.selectedSymbol.set(''); } this.actionStatus.set('Saved simulation deleted.'); }, error: e => this.error.set(e instanceof Error ? e.message : 'Unable to delete saved simulation.'), complete: () => { this.savedLoading.set(false); this.loadSavedList(); } });
  }

  private firstDecisionCandleIndex(stock: StockCapture): number {
    const ready = stock.candles.findIndex(c => this.evaluationReady(c));
    if (ready >= 0) return ready;
    const captured = stock.candles.findIndex(c => {
      const d = c.decision ?? {};
      return String(d['signal'] ?? '').trim() !== '' || d['score'] !== undefined;
    });
    return captured >= 0 ? captured : 0;
  }

  selectStock(symbol: string): void {
    this.selectedSymbol.set(symbol);
    const selectedStock = this.stocks().find(x => x.symbol === symbol);
    this.selectedCandle.set(selectedStock ? this.firstDecisionCandleIndex(selectedStock) : 0);
    this.playIndex.set(0);
    this.replayCursor.set(0);
    this.tickPage.set(0);
    this.invalidateChartCache();
    const s = this.stocks().find((x) => x.symbol === symbol);
    if (s) this.loadParametersFromConfiguration(s.configuration);
    this.runSimulation();
    this.scrollSelectedStockIntoView();
  }
  selectCandle(index: number): void {
    const stock = this.stock();
    const safeIndex = stock?.candles?.length
      ? Math.max(0, Math.min(index, stock.candles.length - 1))
      : 0;
    this.selectedCandle.set(safeIndex);
    this.playIndex.set(0);
    this.replayCursor.set(this.tickIndexForCandle(safeIndex));
    this.refreshSelectedDiagnostics();
  }
  toggle(name: string): void {
    this.toggles[name] = !this.toggles[name];
    this.invalidateChartCache();
  }

  previousTickPage(): void { this.tickPage.set(Math.max(0, this.tickPage() - 1)); }
  nextTickPage(): void { this.tickPage.set(Math.min(this.tickPageCount() - 1, this.tickPage() + 1)); }
  tickPageLabel(): string {
    const total = this.validTicks().length;
    if (!total) return '0 / 0';
    const start = this.tickPage() * this.tickPageSize + 1;
    const end = Math.min(total, start + this.tickPageSize - 1);
    return `${start}-${end} / ${total}`;
  }

  toggleReplayFromChart(): void {
    const stock = this.stock();
    if (!stock) return;

    // A chart tap is intentionally the same play/pause action as the replay button.
    // runLive() resumes from playIndex() when paused and starts over only after completion.
    void this.runLive();
  }

  async runLive(): Promise<void> {
    const stock = this.stock();
    if (!stock) {
      this.replayStatus.set('Upload a lifecycle workbook first.');
      return;
    }
    const ticks = this.validTicks();
    if (!ticks.length) {
      this.replayStatus.set('No valid ticks are available for replay.');
      return;
    }
    if (this.playing()) {
      this.playing.set(false);
      this.replayStatus.set(
        `Paused at tick ${this.playIndex()} of ${ticks.length}.`,
      );
      return;
    }
    if (this.playIndex() >= ticks.length) this.playIndex.set(0);
    this.playing.set(true);
    this.replayStatus.set('Replaying captured ticks…');
    this.activeTab.set('overview');
    for (let i = this.playIndex(); i < ticks.length && this.playing(); i++) {
      const tick = ticks[i];
      this.playIndex.set(i + 1);
      this.replayTick.set(tick);
      this.replayCursor.set(i);
      const candleIndex = this.candleIndexForTick(tick);
      if (candleIndex >= 0) this.selectedCandle.set(candleIndex);
      await new Promise((resolve) => setTimeout(resolve, this.replaySpeedMs()));
    }
    const finished = this.playIndex() >= ticks.length;
    this.playing.set(false);
    this.replayStatus.set(
      finished
        ? `Replay complete — ${ticks.length} ticks evaluated.`
        : `Paused at tick ${this.playIndex()} of ${ticks.length}.`,
    );
  }

  setReplaySpeed(value: number): void {
    const speed = Number(value);
    if (Number.isFinite(speed)) this.replaySpeedMs.set(Math.max(20, Math.min(1000, speed)));
  }

  replayPosition(): number {
    return this.replayTicksCount() ? Math.min(this.replayCursor() + 1, this.replayTicksCount()) : 0;
  }

  replaySpeedLabel(): string {
    const ms = this.replaySpeedMs();
    return `${(80 / ms).toFixed(1)}×`;
  }

  seekReplay(index: number): void {
    const stock = this.stock();
    const ticks = this.validTicks();
    if (!ticks.length) return;
    const safe = Math.max(0, Math.min(Math.round(Number(index)), ticks.length - 1));
    const tick = ticks[safe];
    this.replayCursor.set(safe);
    this.playIndex.set(safe);
    this.replayTick.set(tick);
    const candleIndex = this.candleIndexForTick(tick);
    if (candleIndex >= 0) {
      this.selectedCandle.set(candleIndex);
      this.refreshSelectedDiagnostics();
    }
    this.replayStatus.set(`Position ${safe + 1} of ${ticks.length} · ${this.istTime(tick.utc || tick.exchangeTime)}`);
  }

  tickIndexForCandle(index: number): number {
    const stock = this.stock();
    if (!stock) return 0;
    const candle = stock.candles[index];
    if (!candle?.ticks?.length) return 0;
    const target = candle.ticks[0];
    const ticks = this.validTicks();
    return Math.max(0, ticks.findIndex((t) => t.n === target.n && t.sequence === target.sequence));
  }

  replayTicksCount(): number { return this.validTicks().length; }

  readableIndicatorLabel(key: string): string {
    const labels: Record<string,string> = {
      EMA9:'EMA 9', EMA21:'EMA 21', EMA50:'EMA 50', EMA200:'EMA 200', VWAP:'VWAP', AnchoredVWAP:'Anchored VWAP',
      SuperTrend:'SuperTrend', BollingerUpper:'Bollinger Upper', BollingerMiddle:'Bollinger Middle', BollingerLower:'Bollinger Lower',
      RSI:'RSI', MACD:'MACD', MACDSignal:'MACD Signal', MACDHistogram:'MACD Histogram', ADX:'ADX', RelativeVolume:'Relative Volume',
      ATR:'ATR', Choppiness:'Choppiness', EMASlope9:'EMA Slope 9', EMASlope21:'EMA Slope 21', PullbackDistance:'Pullback Distance',
      DistanceFromEMA:'Distance from EMA', DistanceFromVWAP:'Distance from VWAP', SpreadPercent:'Spread %', Score:'Score',
      AdaptiveEdgeScore:'Adaptive Edge Score', AdaptiveRiskReward:'Adaptive Risk / Reward', AdaptiveExpectedNetValue:'Adaptive Expected Net Value'
    };
    return labels[key] ?? key;
  }

  selectedIndicatorEntries(): Array<{key:string; label:string; value:string}> {
    const keys = [...this.chartOverlayKeys(), ...this.chartOscillatorKeys(), 'Score', 'Confidence', 'AdaptiveEdgeScore', 'AdaptiveRiskReward', 'AdaptiveExpectedNetValue', 'SpreadPercent'];
    return [...new Set(keys)].map(key => {
      const raw = this.indicatorLookup(this.indicators(), key) ?? this.indicatorLookup(this.replayTick()?.indicators, key);
      return { key, label: this.readableIndicatorLabel(key), value: raw === undefined || raw === '' ? '—' : typeof raw === 'number' ? raw.toFixed(3) : String(raw) };
    }).filter(x => x.value !== '—');
  }

  analysisSummary(): Array<{label:string; value:string; detail:string}> {
    const r = this.simulation();
    const total = r.trades + r.missed;
    const winRate = r.trades ? (r.wins / r.trades) * 100 : 0;
    return [
      {label:'Net result', value:`₹${r.netProfit.toFixed(2)}`, detail:r.netProfit >= 0 ? 'Profitable under replay' : 'Loss under replay'},
      {label:'Trade outcome', value:`${r.trades} trades`, detail:`${r.wins} wins · ${r.losses} losses · ${winRate.toFixed(1)}% win rate`},
      {label:'Opportunities', value:`${r.missed} missed`, detail:`${total ? r.opportunityPercent.toFixed(1) : '0.0'}% opportunity miss rate`},
      {label:'Risk quality', value:`${r.avoidableLosses} avoidable`, detail:`${r.actualLosses} actual loss(es) identified`},
      {label:'Missed profit', value:`₹${r.missedProfit.toFixed(2)}`, detail:'Estimated opportunity left uncaptured'},
      {label:'Best entry', value:r.bestEntry ? `₹${r.bestEntry.toFixed(2)}` : '—', detail:this.istTime(r.bestEntryTime)}
    ];
  }

  private yieldToUi(): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, 0));
  }

  runSimulationFromUi(): void {
    if (!this.stock()) {
      this.replayStatus.set(
        'Upload a lifecycle workbook before running a simulation.',
      );
      return;
    }
    this.actionBusy.set(true);
    this.busyAction.set('simulation');
    this.actionStatus.set('Running simulation…');
    setTimeout(() => {
      try {
        this.runSimulation();
        this.simulationCompleted.set(true);
        this.activeTab.set('simulation');
        this.actionStatus.set(
          `Simulation complete — ${this.simulation().trades} accepted trade(s), ${this.simulation().missed} missed opportunity(ies).`,
        );
      } catch (e) {
        this.error.set(e instanceof Error ? e.message : 'Simulation failed.');
        this.actionStatus.set('Simulation failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }

  optimizeFromUi(): void {
    this.runLongAction(
      'stock-config',
      'Finding the best configuration for the selected stock…',
      () => this.optimize(),
    );
  }

  optimizeGlobalFromUi(): void {
    this.runLongAction(
      'global-config',
      'Searching for a global configuration across all stocks…',
      () => this.optimizeGlobal(),
    );
  }

  learnGlobalFromUi(): void {
    this.runLongAction(
      'global-learning',
      'Learning global dynamics from captured decisions…',
      () => this.learnGlobalDynamics(),
    );
  }

  learnRegimeFromUi(): void {
    this.runLongAction('regime-learning', 'Learning regime-specific policies…', () =>
      this.learnRegimeDynamics(),
    );
  }

  private runLongAction(actionKey: string, message: string, action: () => void): void {
    if (!this.stock()) {
      this.replayStatus.set(
        'Upload a lifecycle workbook before starting this operation.',
      );
      return;
    }
    this.actionBusy.set(true);
    this.busyAction.set(actionKey);
    this.actionStatus.set(message);
    setTimeout(() => {
      try {
        action();
        this.activeTab.set('simulation');
        this.actionStatus.set(
          'Operation complete. Review the diagnostic result below.',
        );
      } catch (e) {
        this.error.set(e instanceof Error ? e.message : 'Operation failed.');
        this.actionStatus.set('Operation failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }

  runSimulation(): void {
    const stock = this.stock();
    if (!stock) return;
    const allCandles = stock.candles;
    const candles = allCandles.filter(
      (c) => c.candle.close > 0 && this.evaluationReady(c),
    );
    this.evaluatedObservationCount.set(candles.length);
    this.skippedObservationCount.set(
      Math.max(0, allCandles.length - candles.length),
    );
    const issues: DiagnosticIssue[] = [];
    const reasons = new Map<string, number>();
    let net = 0,
      trades = 0,
      wins = 0,
      losses = 0,
      missed = 0,
      actualLosses = 0,
      avoidableLosses = 0,
      missedProfit = 0;
    let bestEntry = 0,
      bestExit = 0,
      bestEntryTime = '',
      bestExitTime = '';
    const allTicks = stock.candles
      .flatMap((c) => c.ticks)
      .filter((t) => t.ltp > 0)
      .sort((a, b) => this.time(a) - this.time(b));
    const globalPair = this.bestPair(allTicks);
    if (globalPair) {
      bestEntry = globalPair.entry;
      bestExit = globalPair.exit;
      bestEntryTime = globalPair.entryTime;
      bestExitTime = globalPair.exitTime;
    }
    const recommendationEvidence = new Map<
      string,
      { count: number; gain: number; values: number[] }
    >();
    // A workbook row is a tick-level observation, not necessarily a distinct
    // trading opportunity. Consecutive blocked observations with the same first
    // gate are one opportunity episode; otherwise a single move can be counted
    // dozens of times.
    const missedEpisodes: Array<{
      gate: string;
      index: number;
      timestamp: string;
      gainPct: number;
      missedValue: number;
    }> = [];
    let lastMissedIndex = -2;
    let lastMissedGate = '';
    const exitIssueKeys = new Set<string>();
    let lastExitIssueIndex = -2;
    let lastExitSuggested = 0;

    candles.forEach((c) => {
      const sourceTick: any = c.ticks?.[c.ticks.length - 1];
      const d: any = { ...(c.decision ?? {}) };
      const p = c.candle.close;
      const score = this.num(
        d['score'] ??
          this.indicatorLookup(sourceTick?.indicators, 'Score') ??
          this.indicatorLookup(c.indicators, 'Score'),
      );
      const conf = this.num(
        d['adaptiveConfidence'] ?? sourceTick?.confidence ??
          this.indicatorLookup(sourceTick?.indicators, 'Confidence'),
      );
      const rr = this.num(
        d['adaptiveRiskReward'] ?? sourceTick?.adaptiveRiskReward ??
          this.indicatorLookup(sourceTick?.indicators, 'AdaptiveRiskReward'),
      );
      const edge = this.num(
        d['adaptiveEdgeScore'] ?? sourceTick?.adaptiveEdgeScore ??
          this.indicatorLookup(sourceTick?.indicators, 'AdaptiveEdgeScore'),
      );
      const spread = this.num(
        this.indicatorLookup(c.indicators, 'SpreadPercent') ??
          sourceTick?.spreadPct,
      );
      const technicalBuy = String(d['signal'] ?? sourceTick?.decision ?? this.indicatorLookup(sourceTick?.indicators, 'Signal') ?? '').toUpperCase() === 'BUY';
      const scorePass = score >= this.params.minimumScore,
        confPass =
          !this.params.useAdaptiveScore ||
          conf >= this.params.minimumConfidence,
        rrPass =
          !this.params.useAdaptiveScore || rr >= this.params.minimumRiskReward,
        edgePass =
          !this.params.useAdaptiveScore || edge >= this.params.minimumEdgeScore,
        spreadPass = spread <= this.params.maximumSpreadPercent;
      const passes =
        technicalBuy &&
        scorePass &&
        confPass &&
        rrPass &&
        edgePass &&
        spreadPass;
      const future = this.futureTicks(allCandles, c.index);
      const horizon = future.length ? future : c.ticks;
      if (!horizon.length) return;
      const futureBest = Math.max(p, ...horizon.map((t) => t.ltp));
      const futureWorst = Math.min(p, ...horizon.map((t) => t.ltp));
      const gainPct = p ? ((futureBest - p) / p) * 100 : 0;
      const lossPct = p ? ((futureWorst - p) / p) * 100 : 0;
      const netPct = gainPct - this.params.minimumProfitPercent;
      const gateList = this.gates(
        d,
        score,
        conf,
        rr,
        edge,
        spread,
        technicalBuy,
        scorePass,
        confPass,
        rrPass,
        edgePass,
        spreadPass,
      );
      if (passes && gainPct > 0) {
        trades++;
        net += (p * netPct) / 100;
        if (netPct > 0) wins++;
        else losses++;
      }
      if (!passes && netPct > 0) {
        const gate = gateList[0] ?? 'Configuration gate';
        const value = (p * netPct) / 100;
        const sameEpisode =
          c.index === lastMissedIndex + 1 && gate === lastMissedGate;
        if (sameEpisode && missedEpisodes.length) {
          const episode = missedEpisodes[missedEpisodes.length - 1];
          // Retain the most valuable point in the episode rather than adding
          // every tick's opportunity value repeatedly.
          if (value > episode.missedValue) {
            episode.missedValue = value;
            episode.gainPct = gainPct;
            episode.timestamp = c.timestamp;
          }
        } else {
          missedEpisodes.push({
            gate,
            index: c.index,
            timestamp: c.timestamp,
            gainPct,
            missedValue: value,
          });
          reasons.set(gate, (reasons.get(gate) ?? 0) + 1);
          this.recordRecommendation(recommendationEvidence, gate, gainPct);
        }
        lastMissedIndex = c.index;
        lastMissedGate = gate;
      } else {
        lastMissedIndex = -2;
        lastMissedGate = '';
      }
      const actual = c.actualTrade ?? {};
      const status = String(actual['Status'] ?? '').toLowerCase();
      const entry = this.num(actual['EntryPrice']);
      const exit = this.num(actual['ExitPrice']);
      const actualNet = this.num(actual['EstimatedNetProfit']);
      if (
        entry > 0 &&
        (status.includes('loss') || actualNet < 0 || (exit > 0 && exit < entry))
      ) {
        actualLosses++;
        const avoidable = this.isAvoidableLoss(c, future);
        if (avoidable) {
          avoidableLosses++;
          issues.push({
            kind: 'LOSING_TRADE',
            timestamp: c.timestamp,
            title: `Losing trade at ${this.format(c.timestamp)}`,
            detail: `Actual trade lost approximately ${actualNet.toFixed(2)}. The captured path shows a safer exit opportunity before the adverse move.`,
            severity: 'high',
            metric: 'Exit timing',
            current: exit,
            suggested: this.bestExitAfterEntry(c, allTicks),
          });
        }
      }
      if (technicalBuy && gainPct > this.params.minimumProfitPercent) {
        const bestExitTick = this.firstPeak(horizon, p);
        if (bestExitTick && (!exit || exit < bestExitTick.ltp)) {
          const actualEntry = this.num(actual['EntryPrice']);
          const actualEntryTime = String(actual['EntryTime'] ?? '');
          const key = actualEntry > 0
            ? `${actualEntryTime}|${actualEntry.toFixed(4)}`
            : `episode|${bestExitTick.ltp.toFixed(4)}`;
          const repeated = exitIssueKeys.has(key) ||
            (actualEntry <= 0 && c.index === lastExitIssueIndex + 1 && Math.abs(bestExitTick.ltp - lastExitSuggested) < 0.0001);
          if (!repeated) {
            exitIssueKeys.add(key);
            lastExitIssueIndex = c.index;
            lastExitSuggested = bestExitTick.ltp;
            issues.push({
              kind: 'EXIT',
              timestamp: c.timestamp,
              title: `Exit opportunity after ${this.format(c.timestamp)}`,
              detail: `Price subsequently reached ₹${bestExitTick.ltp.toFixed(2)} before the observed decline.`,
              severity: 'medium',
              metric: 'Exit price',
              current: exit > 0 ? exit : p,
              suggested: bestExitTick.ltp,
            });
          }
        }
      }
      void futureWorst;
      void lossPct;
    });
    missed = missedEpisodes.length;
    missedProfit = missedEpisodes.reduce((sum, x) => sum + x.missedValue, 0);
    const opportunity = candles.length ? (missed / candles.length) * 100 : 0;
    const top = [...reasons.entries()]
      .sort((a, b) => b[1] - a[1])
      .slice(0, 8)
      .map(([r, n]) => `${r} (${n})`);
    const replay = this.replayProductionDecision(
      stock,
      allCandles,
      this.selectedCandle(),
    );
    const futurePaths = this.analyzeAllTradePaths(stock, allTicks);
    const futurePath = futurePaths[futurePaths.length - 1];
    const recommendations = this.buildRecommendations(recommendationEvidence);
    const recommendation = this.recommendation(
      top,
      recommendations,
      missed,
      actualLosses,
      avoidableLosses,
    );
    this.simulation.set({
      netProfit: net,
      trades,
      wins,
      losses,
      missed,
      actualLosses,
      avoidableLosses,
      missedProfit,
      bestEntry,
      bestExit,
      bestEntryTime,
      bestExitTime,
      opportunityPercent: opportunity,
      reasons: top,
      recommendation,
      issues: issues.slice(0, 40),
      recommendations,
      replay,
      futurePath,
      futurePaths,
      globalConfiguration: this.globalOptimization() ?? undefined,
    });
    this.refreshSelectedDiagnostics();
  }

  private refreshSelectedDiagnostics(): void {
    const stock = this.stock();
    if (!stock?.candles?.length) return;
    const index = Math.max(
      0,
      Math.min(this.selectedCandle(), stock.candles.length - 1),
    );
    const c = stock.candles[index];
    const replay = this.replayProductionDecision(stock, stock.candles, index);
    if (!replay) return;
    const future = this.futureTicks(stock.candles, index);
    const entry =
      this.num(c.actualTrade?.['EntryPrice']) || this.num(c.candle.close);
    const best = future.length
      ? Math.max(entry, ...future.map((t) => t.ltp))
      : entry;
    const bestTick = future.find((t) => t.ltp === best);
    const worst = future.length
      ? Math.min(entry, ...future.map((t) => t.ltp))
      : entry;
    const d = { ...c.decision };
    d['gateFailures'] = replay.gates
      .filter((g) => g.status === 'FAIL')
      .map((g) => g.name)
      .join(' | ');
    d['replaySignal'] = replay.signal;
    d['replayShouldBuy'] = replay.shouldBuy;
    d['adaptiveRequiredEntryScore'] = replay.appliedPolicy?.minimumScore ?? this.params.minimumScore;
    if (d['adaptiveConfidence'] === undefined && c.ticks[0]?.confidence !== undefined) d['adaptiveConfidence'] = c.ticks[0].confidence;
    if (d['adaptiveRiskReward'] === undefined && c.ticks[0]?.adaptiveRiskReward !== undefined) d['adaptiveRiskReward'] = c.ticks[0].adaptiveRiskReward;
    if (d['adaptiveEdgeScore'] === undefined && c.ticks[0]?.adaptiveEdgeScore !== undefined) d['adaptiveEdgeScore'] = c.ticks[0].adaptiveEdgeScore;
    if (d['adaptiveExpectedNetValue'] === undefined && c.ticks[0]?.adaptiveExpectedNetValue !== undefined) d['adaptiveExpectedNetValue'] = c.ticks[0].adaptiveExpectedNetValue;
    if (!d['score']) {
      const capturedScore = this.indicatorLookup(c.indicators, 'Score');
      if (capturedScore !== undefined) d['score'] = this.num(capturedScore);
    }
    if (!d['signal']) {
      const capturedSignal = this.indicatorLookup(c.indicators, 'Signal') ?? c.ticks[0]?.decision;
      if (capturedSignal !== undefined && String(capturedSignal).trim() !== '') d['signal'] = String(capturedSignal);
    }
    const stop = this.num(d['stopLoss'] ?? c.ticks[0]?.stopLoss);
    const target = this.num(d['targetPrice'] ?? c.ticks[0]?.targetPrice);
    if (stop > 0) d['stopLoss'] = stop;
    if (target > 0) d['targetPrice'] = target;
    c.decision = d;
    this.simulation.update((result) => ({
      ...result,
      bestEntry: entry,
      bestExit: best,
      bestEntryTime: c.timestamp,
      bestExitTime: bestTick?.exchangeTime || bestTick?.utc || c.timestamp,
      replay,
      futurePath: result.futurePath,
      futurePaths: result.futurePaths,
    }));
    void worst;
  }

  decisionDebugSteps(): DecisionDebugStep[] {
    const stock = this.stock();
    const c = this.candle();
    const rp = this.simulation().replay;
    if (!stock || !c || !rp) return [];
    const steps: DecisionDebugStep[] = [];
    let order = 1;
    const time = c.timestamp;
    steps.push({
      order: order++,
      phase: 'SIGNAL',
      time,
      status: rp.signal === 'BUY' ? 'PASS' : 'FAIL',
      title: `Technical signal: ${rp.signal || 'UNKNOWN'}`,
      detail:
        rp.signal === 'BUY'
          ? 'The captured technical path produced a BUY signal.'
          : 'The captured technical path did not produce a BUY signal.',
      impact:
        rp.signal === 'BUY'
          ? 'Eligible for gate evaluation'
          : 'No entry unless the signal itself changes.',
    });
    if (rp.regime) {
      steps.push({
        order: order++,
        phase: 'GATE',
        time,
        status: 'INFO',
        title: `Market regime: ${rp.regime}`,
        detail:
          rp.policyImpact ||
          'Decision-time regime classification completed without using future prices.',
        parameter: 'Policy selection',
        impact: rp.appliedPolicy
          ? `Policy thresholds: score ${rp.appliedPolicy.minimumScore}, confidence ${rp.appliedPolicy.minimumConfidence}, R/R ${rp.appliedPolicy.minimumRiskReward}, edge ${rp.appliedPolicy.minimumEdgeScore}, spread ≤ ${rp.appliedPolicy.maximumSpreadPercent}%. Source: ${rp.policySource || 'BASELINE'}.`
          : undefined,
      });
    }
    for (const g of rp.gates) {
      const parameter = this.debugParameter(g.name);
      const impact =
        g.status === 'FAIL' && g.suggested !== undefined
          ? `Changing ${parameter || g.name} toward ${g.suggested} would make this gate pass in the counterfactual replay.`
          : g.status === 'FAIL'
            ? 'This gate is the current blocker; the captured data does not provide a safe numeric override.'
            : undefined;
      steps.push({
        order: order++,
        phase: 'GATE',
        time,
        status: g.status,
        title: g.name,
        detail: g.reason,
        parameter,
        current: g.actual,
        suggested: g.required,
        impact,
      });
    }
    const entryPrice = this.num(c.candle.close);
    const future = this.futureTicks(stock.candles, c.index);
    const firstFuture = future.find((t) => t.ltp > entryPrice);
    const worst = future
      .filter((t) => t.ltp > 0)
      .reduce((m, t) => Math.min(m, t.ltp), entryPrice);
    if (rp.shouldBuy) {
      steps.push({
        order: order++,
        phase: 'ENTRY',
        time,
        status: 'PASS',
        title: 'Entry accepted',
        detail: `Replay would admit the position around ₹${entryPrice.toFixed(2)}.`,
        impact: firstFuture
          ? `First favorable tick ₹${firstFuture.ltp.toFixed(2)} at ${this.format(firstFuture.exchangeTime || firstFuture.utc)}.`
          : undefined,
      });
    } else {
      steps.push({
        order: order++,
        phase: 'ENTRY',
        time,
        status: 'FAIL',
        title: 'Entry blocked',
        detail: `The live-equivalent decision remains blocked at this observation.`,
        impact: rp.firstBlockingGate
          ? `Primary intervention: ${rp.firstBlockingGate}.`
          : undefined,
      });
    }
    if (future.length) {
      const best = Math.max(...future.map((t) => t.ltp));
      const bestTick = future.find((t) => t.ltp === best);
      steps.push({
        order: order++,
        phase: 'FUTURE',
        time: bestTick?.exchangeTime || bestTick?.utc || time,
        status: 'INFO',
        title: 'Future-price evidence',
        detail: `From this decision onward price reached ₹${best.toFixed(2)} and fell as low as ₹${worst.toFixed(2)}.`,
        impact: `Maximum favorable move ${(entryPrice ? ((best - entryPrice) / entryPrice) * 100 : 0).toFixed(2)}%; maximum adverse move ${(entryPrice ? ((worst - entryPrice) / entryPrice) * 100 : 0).toFixed(2)}%.`,
      });
    }
    if (rp.exitReplay) {
      const er = rp.exitReplay;
      steps.push({
        order: order++,
        phase: 'EXIT',
        time: er.bestFutureExitTime || time,
        status: er.prematureExit ? 'FAIL' : 'PASS',
        title: er.prematureExit
          ? 'Recorded exit may be premature'
          : 'Exit path consistent with captured evidence',
        detail: er.diagnosis,
        impact: er.bestFutureExitPrice
          ? `Counterfactual best future exit in captured path: ₹${er.bestFutureExitPrice.toFixed(2)}.`
          : undefined,
      });
    }
    const f = this.simulation().futurePath;
    if (f) {
      steps.push({
        order: order++,
        phase: 'OUTCOME',
        time: f.bestExitTime || f.exitTime || time,
        status: 'INFO',
        title: 'Outcome attribution',
        detail: `Realized ${(f.realizedPercent || 0).toFixed(2)}% versus a captured best exit of ₹${f.bestExitPrice.toFixed(2)}.`,
        impact: `Profit left on table: ${(f.leftOnTablePercent || 0).toFixed(2)}%.`,
      });
    }
    return steps;
  }

  private debugParameter(name: string): string | undefined {
    const n = name.toLowerCase();
    if (n.includes('score')) return 'minimumScore';
    if (n.includes('confidence')) return 'minimumConfidence';
    if (n.includes('risk') || n.includes('reward')) return 'minimumRiskReward';
    if (n.includes('edge')) return 'minimumEdgeScore';
    if (n.includes('spread')) return 'maximumSpreadPercent';
    return undefined;
  }

  optimize(): void {
    const original = { ...this.params };
    let best = {
      net: -Infinity,
      score: this.params.minimumScore,
      confidence: this.params.minimumConfidence,
      rr: this.params.minimumRiskReward,
      edge: this.params.minimumEdgeScore,
    };
    for (const score of [55, 60, 65, 70, 75, 80])
      for (const confidence of [45, 55, 65, 75])
        for (const rr of [1, 1.25, 1.5, 1.75, 2])
          for (const edge of [35, 45, 55]) {
            this.params.minimumScore = score;
            this.params.minimumConfidence = confidence;
            this.params.minimumRiskReward = rr;
            this.params.minimumEdgeScore = edge;
            this.runSimulation();
            if (this.simulation().netProfit > best.net)
              best = {
                net: this.simulation().netProfit,
                score,
                confidence,
                rr,
                edge,
              };
          }
    this.params.minimumScore = best.score;
    this.params.minimumConfidence = best.confidence;
    this.params.minimumRiskReward = best.rr;
    this.params.minimumEdgeScore = best.edge;
    this.runSimulation();
    void original;
  }

  optimizeGlobal(): void {
    const current = { ...this.params };
    const baseline = this.evaluateGlobal(current);
    let best = { ...current, objective: this.objective(baseline) };
    const scores = [55, 60, 65, 70, 75, 80],
      confidences = [45, 55, 65, 75],
      rrs = [1, 1.25, 1.5, 1.75, 2],
      edges = [35, 45, 55];
    for (const minimumScore of scores)
      for (const minimumConfidence of confidences)
        for (const minimumRiskReward of rrs)
          for (const minimumEdgeScore of edges) {
            const candidate = {
              ...current,
              minimumScore,
              minimumConfidence,
              minimumRiskReward,
              minimumEdgeScore,
            };
            const result = this.evaluateGlobal(candidate);
            const objective = this.objective(result);
            if (objective > best.objective) best = { ...candidate, objective };
          }
    this.params.minimumScore = best.minimumScore;
    this.params.minimumConfidence = best.minimumConfidence;
    this.params.minimumRiskReward = best.minimumRiskReward;
    this.params.minimumEdgeScore = best.minimumEdgeScore;
    const optimized = this.evaluateGlobal(this.params);
    const improvement = optimized.netProfit - baseline.netProfit;
    const confidence =
      optimized.observations >= 200 &&
      optimized.missed <= Math.max(1, baseline.missed * 0.35)
        ? 'HIGH'
        : optimized.observations >= 60
          ? 'MEDIUM'
          : 'LOW';
    const split = this.evaluateGlobalSplit(this.params);
    const result: GlobalConfigurationResult = {
      stocks: this.stocks().length,
      observations: optimized.observations,
      profitableOpportunities: optimized.profitableOpportunities,
      currentNetProfit: baseline.netProfit,
      optimizedNetProfit: optimized.netProfit,
      improvement,
      currentMissed: baseline.missed,
      optimizedMissed: optimized.missed,
      currentLosses: baseline.losses,
      optimizedLosses: optimized.losses,
      configuration: {
        minimumScore: this.params.minimumScore,
        minimumConfidence: this.params.minimumConfidence,
        minimumRiskReward: this.params.minimumRiskReward,
        minimumEdgeScore: this.params.minimumEdgeScore,
        maximumSpreadPercent: this.params.maximumSpreadPercent,
        minimumProfitPercent: this.params.minimumProfitPercent,
      },
      confidence,
      rationale: `Global proposal searched ${scores.length * confidences.length * rrs.length * edges.length} bounded configurations across ${this.stocks().length} stocks, then replayed the selected configuration on a chronological holdout. Future prices are used only to score the counterfactual outcome, never to manufacture the decision inputs.`,
      validation: split,
      proposalId: `GLOBAL-${new Date()
        .toISOString()
        .replace(/[-:.TZ]/g, '')
        .slice(0, 14)}`,
    };
    this.globalOptimization.set(result);
    this.runSimulation();
  }

  createConfigurationProposal(): void {
    if (!this.stocks().length) return;
    if (!this.globalLearning()) this.learnGlobalDynamics();
    const gl = this.globalLearning();
    if (!gl) return;
    const baseline = { ...this.params };
    const recommended = {
      ...gl.recommended,
      useAdaptiveScore: this.params.useAdaptiveScore,
    };
    const hold = gl.holdout;
    const train = this.evaluateGlobal(recommended);
    const changes = gl.parameterEvidence
      .filter((e) => e.current !== e.learned)
      .map((e) => ({
        parameter: e.parameter,
        from: e.current,
        to: e.learned,
        evidence: e.evidence,
        expectedImpact: e.impact,
        confidence: e.confidence,
      }));
    const regimePolicies = this.regimeLearning()?.regimes ?? [];
    const validation = {
      trainImprovement: train.netProfit - gl.baseline.netProfit,
      holdoutImprovement: hold.improvement,
      holdoutLossChange: hold.losses - gl.baseline.losses,
      stockCoveragePercent: gl.robustness.stockCoveragePercent,
      positiveHoldoutStocks: gl.robustness.positiveHoldoutStocks,
      totalHoldoutStocks: gl.robustness.totalHoldoutStocks,
    };
    const validated =
      validation.holdoutImprovement > 0 &&
      validation.holdoutLossChange <= 0 &&
      validation.stockCoveragePercent >= 40;
    const proposal: ConfigurationProposal = {
      proposalId: `CFG-${new Date()
        .toISOString()
        .replace(/[-:.TZ]/g, '')
        .slice(0, 14)}-${Math.floor(Math.random() * 900 + 100)}`,
      version: 1,
      createdAt: new Date().toISOString(),
      status: validated ? 'VALIDATED' : 'PROPOSED',
      scope: regimePolicies.length ? 'REGIME_AWARE' : 'GLOBAL',
      baseline: {
        minimumScore: baseline.minimumScore,
        minimumConfidence: baseline.minimumConfidence,
        minimumRiskReward: baseline.minimumRiskReward,
        minimumEdgeScore: baseline.minimumEdgeScore,
        maximumSpreadPercent: baseline.maximumSpreadPercent,
        minimumProfitPercent: baseline.minimumProfitPercent,
      },
      recommended,
      validation,
      changes,
      regimePolicies,
      rollback: {
        trigger:
          'Rollback if a live/paper evaluation window produces net improvement below the baseline tolerance or increases losses beyond the allowed threshold.',
        baselineNetProfit: gl.baseline.netProfit,
        minimumAllowedImprovement: 0,
        maximumAllowedLossIncrease: 0,
        evaluationWindowDays: 5,
      },
      rationale: `Versioned ${regimePolicies.length ? 'regime-aware' : 'global'} proposal generated from captured decision evidence. It is validated on a chronological holdout and is not automatically promoted to live trading. Use the rollback rule before any production adoption.`,
    };
    this.configurationProposal.set(proposal);
  }

  indicatorColor(key: string): string {
    const colors: Record<string,string> = { EMA9:'#7dd3fc', EMA21:'#a78bfa', EMA50:'#fbbf24', EMA200:'#fb7185', VWAP:'#34d399', AnchoredVWAP:'#22d3ee', SuperTrend:'#f97316', Bollinger:'#c4b5fd', BollingerUpper:'#c4b5fd', BollingerMiddle:'#c4b5fd', BollingerLower:'#c4b5fd', RSI:'#8fa2ff', MACD:'#8fa2ff', ADX:'#8fa2ff' };
    return colors[key] ?? '#8fa2ff';
  }

  explainEntryDecision(): void {
    const stock = this.stock(); const c = this.candle(); const rp = this.simulation().replay;
    if (!stock || !c || !rp) { this.entryExplanation.set('No entry decision is available for the selected candle.'); return; }
    const price = this.num(c.actualTrade?.['EntryPrice']) || this.num(c.candle.close);
    const failed = rp.gates.filter(g => g.status === 'FAIL'); const passed = rp.gates.filter(g => g.status === 'PASS'); const unknown = rp.gates.filter(g => g.status === 'UNKNOWN');
    const blocker = rp.firstBlockingGate || failed[0]?.name || 'No blocking gate recorded';
    const future = this.futureTicks(stock.candles, c.index).filter(t => t.ltp > 0);
    const best = future.length ? Math.max(price, ...future.map(t => t.ltp)) : price; const worst = future.length ? Math.min(price, ...future.map(t => t.ltp)) : price;
    const gain = price > 0 ? ((best - price) / price) * 100 : 0; const adverse = price > 0 ? ((worst - price) / price) * 100 : 0;
    const actual = c.actualTrade ?? {}; const actualEntry = this.num(actual['EntryPrice']); const actualExit = this.num(actual['ExitPrice']);
    const indicatorValues = this.selectedIndicatorEntries().slice(0, 10).map(x => `${x.label}: ${x.value}`).join(' · ');
    const gateSummary = rp.gates.map(g => `${g.status} ${g.name}${g.actual !== undefined ? ` (${this.displayNumber(g.actual,2)} vs ${this.displayNumber(g.required,2)})` : ''}`).join(' | ');
    const lines = [
      `Decision time: ${this.istTime(c.timestamp)} IST`, `Stock: ${stock.symbol} · Decision price: ₹${price.toFixed(2)}`,
      `Technical signal: ${rp.signal || 'UNKNOWN'} · Replay decision: ${rp.shouldBuy ? 'BUY / ACCEPTED' : 'BLOCKED'}`,
      `Decision-time regime: ${rp.regime || 'UNKNOWN'} · Policy source: ${rp.policySource || 'BASELINE'}`, '',
      rp.shouldBuy ? `Why it was accepted: the replay found no blocking gate. ${passed.length} gate(s) passed${unknown.length ? ` and ${unknown.length} could not be verified` : ''}.` : `Why it was blocked: the first blocking gate was “${blocker}”. ${failed.length} gate(s) failed${unknown.length ? ` and ${unknown.length} could not be verified` : ''}.`,
      rp.gates.find(g => g.name === blocker)?.reason ? `Primary blocker evidence: ${rp.gates.find(g => g.name === blocker)?.reason}` : '',
      `Gate-by-gate evidence: ${gateSummary || 'No gate details captured.'}`, '',
      `Future-price attribution: ${future.length ? `the captured path reached ₹${best.toFixed(2)} (${gain >= 0 ? '+' : ''}${gain.toFixed(2)}%) and fell as low as ₹${worst.toFixed(2)} (${adverse.toFixed(2)}%).` : 'No future tick path was available.'}`,
      `Important: future prices are used only to explain the outcome and are not used to manufacture or alter the original decision inputs.`, '',
      `Captured trade state: ${actualEntry > 0 ? `entry ₹${actualEntry.toFixed(2)}` : 'no captured entry price'}${actualExit > 0 ? ` · exit ₹${actualExit.toFixed(2)}` : ''}${actual['Status'] ? ` · status ${String(actual['Status'])}` : ''}.`,
      `Selected indicators: ${indicatorValues || 'No indicator values available at this candle.'}`,
      rp.entryMistake ? `Entry assessment: ${rp.entryMistake}` : '', rp.exitMistake ? `Exit assessment: ${rp.exitMistake}` : '',
      rp.parityNotes?.length ? `Production parity notes: ${rp.parityNotes.join(' | ')}` : '',
    ].filter(Boolean).join('\n');
    this.entryExplanation.set(lines);
  }
  clearEntryExplanation(): void { this.entryExplanation.set(''); }

  exportSimulationToExcel(): void {
    const s = this.stock();
    if (!s) {
      this.error.set('Upload a lifecycle workbook before exporting simulation data.');
      return;
    }

    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('export-excel');
    this.actionStatus.set('Preparing Excel export…');

    // Defer the heavy workbook construction so Angular can render the loader first.
    setTimeout(() => {
      try {
        const r = this.simulation();
        const wb = XLSX.utils.book_new();

        const addSheet = (name: string, rows: unknown[]): void => {
          const safeName = this.excelSheetName(name, wb);
          const data = Array.isArray(rows) && rows.length ? rows : [{ Info: 'No data available' }];
          const ws = XLSX.utils.json_to_sheet(data as Record<string, unknown>[]);
          XLSX.utils.book_append_sheet(wb, ws, safeName);
        };

        addSheet('Summary', [
          { Metric: 'Symbol', Value: s.symbol },
          { Metric: 'Token', Value: s.token },
          { Metric: 'Exchange', Value: s.exchange },
          { Metric: 'Net Profit', Value: r.netProfit },
          { Metric: 'Trades', Value: r.trades },
          { Metric: 'Wins', Value: r.wins },
          { Metric: 'Losses', Value: r.losses },
          { Metric: 'Missed Opportunities', Value: r.missed },
          { Metric: 'Missed Profit', Value: r.missedProfit },
          { Metric: 'Avoidable Losses', Value: r.avoidableLosses },
          { Metric: 'Actual Losses', Value: r.actualLosses },
          { Metric: 'Opportunity %', Value: r.opportunityPercent },
          { Metric: 'Best Entry', Value: r.bestEntry },
          { Metric: 'Best Exit', Value: r.bestExit },
          { Metric: 'Best Entry Time IST', Value: this.istTime(r.bestEntryTime) },
          { Metric: 'Best Exit Time IST', Value: this.istTime(r.bestExitTime) },
          { Metric: 'Recommendation', Value: r.recommendation },
        ]);

        addSheet('Parameters', Object.entries(this.params).map(([Key, Value]) => ({ Key, Value })));
        addSheet('Configuration', Object.entries(s.configuration ?? {}).map(([Key, Value]) => ({ Key, Value: this.exportExcelValue(Value) })));

        addSheet('Candles', s.candles.map((c, i) => ({
          Index: i,
          TimestampIST: this.istTime(c.timestamp),
          Open: c.candle.open,
          High: c.candle.high,
          Low: c.candle.low,
          Close: c.candle.close,
          Volume: c.candle.volume,
          Signal: String(c.decision?.['signal'] ?? ''),
          Score: this.num(c.decision?.['score']),
          Confidence: this.num(c.decision?.['adaptiveConfidence']),
          Edge: this.num(c.decision?.['adaptiveEdgeScore']),
          RiskReward: this.num(c.decision?.['adaptiveRiskReward']),
          Reason: String(c.decision?.['reason'] ?? ''),
        })));

        const indicatorRows = s.candles.flatMap((c, candleIndex) => {
          const values = c.indicators ?? {};
          return Object.entries(values).map(([Indicator, Value]) => ({
            CandleIndex: candleIndex,
            TimestampIST: this.istTime(c.timestamp),
            Indicator,
            Value: this.exportExcelValue(Value),
          }));
        });
        addSheet('Indicators', indicatorRows);

        const decisionRows = s.candles.map((c, i) => ({
          CandleIndex: i,
          TimestampIST: this.istTime(c.timestamp),
          Decision: this.exportExcelValue(c.decision),
          LoadedDecisionFields: (c.loadedDecisionFields ?? []).join(', '),
        }));
        addSheet('Decisions', decisionRows);

        addSheet('Virtual Trades', s.candles.map((c, i) => ({
          CandleIndex: i,
          TimestampIST: this.istTime(c.timestamp),
          VirtualTrade: this.exportExcelValue(c.virtualTrade),
        })));

        addSheet('Actual Trades', s.candles.map((c, i) => ({
          CandleIndex: i,
          TimestampIST: this.istTime(c.timestamp),
          ActualTrade: this.exportExcelValue(c.actualTrade),
        })));

        // Keep one row per tick. JSON-stringify nested/optional fields so every
        // captured value remains exportable without creating invalid worksheet cells.
        const ticks = s.candles.flatMap((c, candleIndex) => c.ticks.map(t => ({
          CandleIndex: candleIndex,
          Tick: t.n,
          Sequence: t.sequence,
          TimeIST: this.istTime(t.exchangeTime || t.utc),
          UTC: t.utc,
          ExchangeTime: t.exchangeTime,
          Stage: t.stage ?? '',
          Decision: t.decision ?? '',
          Status: t.status ?? '',
          Price: t.ltp,
          Bid: t.bid,
          Ask: t.ask,
          Spread: t.spread,
          SpreadPct: t.spreadPct,
          Open: t.open,
          High: t.high,
          Low: t.low,
          Close: t.close,
          LTQ: t.ltq,
          AveragePrice: t.avgPrice,
          DayVolume: t.dayVolume,
          BuyQty: t.buyQty,
          SellQty: t.sellQty,
          MovementScore: t.movementScore ?? '',
          TrendStrength: t.trendStrength ?? '',
          TrendStability: t.trendStability ?? '',
          RecoveryScore: t.recoveryScore ?? '',
          BreakoutStrength: t.breakoutStrength ?? '',
          NoiseScore: t.noiseScore ?? '',
          TickQualityScore: t.tickQualityScore ?? '',
          Confidence: t.confidence ?? '',
          AdaptiveExpectedNetValue: t.adaptiveExpectedNetValue ?? '',
          AdaptiveEdgeScore: t.adaptiveEdgeScore ?? '',
          AdaptiveRiskReward: t.adaptiveRiskReward ?? '',
          EntryPrice: t.entryPrice ?? '',
          ExitPrice: t.exitPrice ?? '',
          StopLoss: t.stopLoss ?? '',
          TargetPrice: t.targetPrice ?? '',
          ExitReason: t.exitReason ?? '',
          DecisionReason: t.decisionReason ?? '',
        })));

        // Split very large tick exports across sheets to avoid Excel's worksheet
        // row limit while keeping every tick.
        const chunkSize = 50000;
        for (let offset = 0, part = 1; offset < ticks.length || part === 1; offset += chunkSize, part++) {
          const chunk = ticks.slice(offset, offset + chunkSize);
          addSheet(`Ticks ${part}`, chunk);
          if (offset >= ticks.length) break;
        }

        const tickIndicatorRows = s.candles.flatMap((c, candleIndex) =>
          c.ticks.flatMap(t => Object.entries(t.indicators ?? {}).map(([Indicator, Value]) => ({
            CandleIndex: candleIndex,
            Tick: t.n,
            TimeIST: this.istTime(t.exchangeTime || t.utc),
            Indicator,
            Value: this.exportExcelValue(Value),
          })))
        );
        // Tick indicators can also exceed Excel's worksheet row limit for
        // high-frequency captures, so partition them exactly like tick rows.
        const indicatorChunkSize = 50000;
        for (let offset = 0, part = 1; offset < tickIndicatorRows.length || part === 1; offset += indicatorChunkSize, part++) {
          addSheet(`Tick Indicators ${part}`, tickIndicatorRows.slice(offset, offset + indicatorChunkSize));
          if (offset >= tickIndicatorRows.length) break;
        }

        addSheet('Issues', r.issues ?? []);
        addSheet('Recommendations', r.recommendations ?? []);
        addSheet('Simulation Details', [
          { Field: 'Reasons', Value: this.exportExcelValue(r.reasons) },
          { Field: 'Recommendation', Value: this.exportExcelValue(r.recommendation) },
          { Field: 'Replay', Value: this.exportExcelValue(r.replay) },
          { Field: 'Future Path', Value: this.exportExcelValue(r.futurePath) },
          { Field: 'Future Paths', Value: this.exportExcelValue(r.futurePaths) },
          { Field: 'Global Configuration', Value: this.exportExcelValue(r.globalConfiguration) },
          { Field: 'Global Learning', Value: this.exportExcelValue(this.globalLearning()) },
          { Field: 'Regime Learning', Value: this.exportExcelValue(this.regimeLearning()) },
          { Field: 'Configuration Proposal', Value: this.exportExcelValue(this.configurationProposal()) },
        ]);

        // Keep an explicit, lossless representation of every captured object.
        // The JSON is chunked below Excel's 32,767-character cell limit so no
        // nested/optional fields are silently lost.
        const completeRows: Array<{ Dataset: string; CandleIndex: number; Tick?: number; TimestampIST: string; Chunk: number; Data: string }> = [];
        const appendJsonChunks = (dataset: string, candleIndex: number, tick: number | undefined, timestamp: string, value: unknown): void => {
          const rawValue = JSON.stringify(value ?? null, (_key, v) => {
            if (v === undefined) return null;
            if (typeof v === 'bigint') return v.toString();
            return v;
          });
          const chunkSize = 30000;
          for (let i = 0, chunk = 1; i < rawValue.length || chunk === 1; i += chunkSize, chunk++) {
            completeRows.push({
              Dataset: dataset,
              CandleIndex: candleIndex,
              ...(tick === undefined ? {} : { Tick: tick }),
              TimestampIST: timestamp,
              Chunk: chunk,
              Data: rawValue.slice(i, i + chunkSize),
            });
            if (i >= rawValue.length) break;
          }
        };
        s.candles.forEach((c, candleIndex) => {
          appendJsonChunks('Candle', candleIndex, undefined, this.istTime(c.timestamp), c);
          c.ticks.forEach(t => appendJsonChunks('Tick', candleIndex, t.n, this.istTime(t.exchangeTime || t.utc), t));
        });
        const completeChunkSize = 50000;
        for (let offset = 0, part = 1; offset < completeRows.length || part === 1; offset += completeChunkSize, part++) {
          addSheet(`Complete Data ${part}`, completeRows.slice(offset, offset + completeChunkSize));
          if (offset >= completeRows.length) break;
        }

        const rawPayload = {
          schemaVersion: '1.0',
          exportType: 'trading-simulation-complete',
          generatedAtIST: this.istTime(new Date().toISOString()),
          timezone: 'Asia/Kolkata',
          source: {
            symbol: s.symbol,
            token: s.token,
            exchange: s.exchange,
            configurationSource: s.configurationSource ?? '',
            configurationCapturedAt: s.configurationCapturedAt ?? '',
          },
          simulation: r,
          parameters: this.params,
          indicatorToggles: this.toggles,
          stock: s,
          replay: {
            selectedCandle: this.selectedCandle(),
            replayCursor: this.replayCursor(),
            replaySpeedMs: this.replaySpeedMs(),
            replayStatus: this.replayStatus(),
            replayTick: this.replayTick(),
          },
          globalOptimization: this.globalOptimization(),
          globalLearning: this.globalLearning(),
          regimeLearning: this.regimeLearning(),
          configurationProposal: this.configurationProposal(),
        };

        const raw = JSON.stringify(rawPayload, (_key, value) => {
          if (value === undefined) return null;
          if (typeof value === 'bigint') return value.toString();
          return value;
        });
        const rawChunkSize = 30000;
        const rawRows: Array<{ Chunk: number; Data: string }> = [];
        for (let i = 0, chunk = 1; i < raw.length; i += rawChunkSize, chunk++) {
          rawRows.push({ Chunk: chunk, Data: raw.slice(i, i + rawChunkSize) });
        }
        addSheet('Raw Complete', rawRows);

        XLSX.writeFile(wb, `${s.symbol}-simulation-${this.fileStamp()}.xlsx`, { compression: true });
        this.actionStatus.set(`Simulation Excel exported — ${s.candles.length} candles, ${ticks.length} ticks.`);
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        this.error.set(`Excel export failed: ${message}`);
        this.actionStatus.set('Excel export failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }

  private exportExcelValue(value: unknown): string | number | boolean {
    if (value === null || value === undefined) return '';
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return value;
    try {
      return JSON.stringify(value);
    } catch {
      return String(value);
    }
  }

  private excelSheetName(requested: string, wb: XLSX.WorkBook): string {
    const base = requested.replace(/[\\/?*\[\]:]/g, ' ').trim().slice(0, 31) || 'Sheet';
    const existing = new Set(wb.SheetNames.map(n => n.toLowerCase()));
    if (!existing.has(base.toLowerCase())) return base;
    for (let i = 2; i < 10000; i++) {
      const suffix = ` ${i}`;
      const candidate = `${base.slice(0, 31 - suffix.length)}${suffix}`;
      if (!existing.has(candidate.toLowerCase())) return candidate;
    }
    return `Sheet${wb.SheetNames.length + 1}`.slice(0, 31);
  }

  exportSimulationToPdf(): void {
    const s = this.stock();
    if (!s) return;

    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('export-pdf');
    this.actionStatus.set('Preparing complete PDF report…');

    // Build the report asynchronously so the loading state can render before
    // the potentially large HTML document is assembled.
    setTimeout(() => {
      try {
        const r = this.simulation();
        const esc = (v: unknown): string =>
          String(v ?? '').replace(/[&<>"']/g, ch =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch] as string)
          );
        const json = (v: unknown): string => {
          try {
            return JSON.stringify(v ?? null);
          } catch {
            return String(v ?? '');
          }
        };
        const cell = (v: unknown): string => `<td>${esc(v)}</td>`;
        const header = (cols: string[]): string => `<tr>${cols.map(c => `<th>${esc(c)}</th>`).join('')}</tr>`;

        const summaryRows = [
          ['Symbol', s.symbol], ['Token', s.token], ['Exchange', s.exchange],
          ['Net Profit', r.netProfit], ['Trades', r.trades], ['Wins', r.wins],
          ['Losses', r.losses], ['Missed Opportunities', r.missed],
          ['Missed Profit', r.missedProfit], ['Avoidable Losses', r.avoidableLosses],
          ['Actual Losses', r.actualLosses], ['Opportunity %', r.opportunityPercent],
          ['Best Entry', r.bestEntry], ['Best Exit', r.bestExit],
          ['Best Entry Time IST', this.istTime(r.bestEntryTime)],
          ['Best Exit Time IST', this.istTime(r.bestExitTime)],
          ['Recommendation', r.recommendation],
        ].map(([k, v]) => `<tr>${cell(k)}${cell(v)}</tr>`).join('');

        const issues = (r.issues ?? []).map(i =>
          `<tr>${cell(i.kind)}${cell(i.timestamp ? this.istTime(i.timestamp) : '')}${cell(i.severity)}${cell(i.title)}${cell(i.detail)}</tr>`
        ).join('');

        const recommendations = (r.recommendations ?? []).map(x =>
          `<tr>${cell(x.parameter)}${cell(x.current)}${cell(x.suggested)}${cell(x.reason)}${cell(x.evidenceCount)}${cell(x.expectedImprovement)}</tr>`
        ).join('');

        const futurePaths = (r.futurePaths ?? (r.futurePath ? [r.futurePath] : [])).map(x =>
          `<tr>${cell(this.istTime(x.entryTime))}${cell(x.exitTime ? this.istTime(x.exitTime) : '')}${cell(x.entryPrice)}${cell(x.actualExitPrice)}${cell(x.maxPrice)}${cell(x.minPrice)}${cell(x.bestExitPrice)}${cell(this.istTime(x.bestExitTime))}${cell(x.maxFavorablePercent)}${cell(x.maxAdversePercent)}${cell(x.realizedPercent)}${cell(x.leftOnTablePercent)}${cell(x.diagnosis)}</tr>`
        ).join('');

        const replay = r.replay;
        const replayRows = replay ? [
          ['Signal', replay.signal], ['Should Buy', replay.shouldBuy], ['First Blocking Gate', replay.firstBlockingGate],
          ['Entry Mistake', replay.entryMistake], ['Exit Mistake', replay.exitMistake],
          ['Future Best Price', replay.futureBestPrice], ['Future Worst Price', replay.futureWorstPrice],
          ['Production Parity', replay.productionParity], ['Production Parity Notes', replay.parityNotes?.join(' | ')],
          ['Regime', replay.regime], ['Policy Source', replay.policySource], ['Policy Impact', replay.policyImpact],
        ].map(([k,v]) => `<tr>${cell(k)}${cell(v)}</tr>`).join('') : '';

        // Every captured candle is printed, including its indicators, decision,
        // virtual/actual trade and every tick. This deliberately does not use
        // a top-N/first-N limit.
        const candleSections = s.candles.map((c, i) => {
          const tickRows = c.ticks.map(t =>
            `<tr>${cell(t.n)}${cell(t.sequence)}${cell(this.istTime(t.exchangeTime || t.utc))}${cell(t.stage)}${cell(t.status)}${cell(t.decision)}${cell(t.ltp)}${cell(t.bid)}${cell(t.ask)}${cell(t.spreadPct)}${cell(t.dayVolume)}${cell(t.movementScore)}${cell(t.trendStrength)}${cell(t.trendStability)}${cell(t.recoveryScore)}${cell(t.breakoutStrength)}${cell(t.noiseScore)}${cell(t.tickQualityScore)}${cell(t.confidence)}${cell(t.adaptiveExpectedNetValue)}${cell(t.adaptiveEdgeScore)}${cell(t.adaptiveRiskReward)}${cell(t.entryPrice)}${cell(t.exitPrice)}${cell(t.stopLoss)}${cell(t.targetPrice)}${cell(t.exitReason)}${cell(t.decisionReason)}${cell(json(t.indicators))}</tr>`
          ).join('');
          return `<section class="data-section">
            <h3>Candle ${i + 1} — ${esc(this.istTime(c.timestamp))}</h3>
            <table><tbody>
              <tr>${cell('Timestamp IST')}${cell(this.istTime(c.timestamp))}</tr>
              <tr>${cell('OHLCV')}${cell(`${c.candle.open} / ${c.candle.high} / ${c.candle.low} / ${c.candle.close} / ${c.candle.volume}`)}</tr>
              <tr>${cell('Indicators')}${cell(json(c.indicators))}</tr>
              <tr>${cell('Decision')}${cell(json(c.decision))}</tr>
              <tr>${cell('Loaded Decision Fields')}${cell((c.loadedDecisionFields ?? []).join(', '))}</tr>
              <tr>${cell('Virtual Trade')}${cell(json(c.virtualTrade))}</tr>
              <tr>${cell('Actual Trade')}${cell(json(c.actualTrade))}</tr>
            </tbody></table>
            <h4>Ticks (${c.ticks.length})</h4>
            <table class="wide"><thead>${header(['Tick','Sequence','Time IST','Stage','Status','Decision','Price','Bid','Ask','Spread %','Day Volume','Movement','Trend','Stability','Recovery','Breakout','Noise','Quality','Confidence','Expected Net','Edge','Risk/Reward','Entry','Exit','Stop Loss','Target','Exit Reason','Decision Reason','Indicators'])}</thead>
            <tbody>${tickRows || `<tr><td colspan="29">No ticks captured.</td></tr>`}</tbody></table>
          </section>`;
        }).join('');

        const html = `<!doctype html><html><head><title>${esc(s.symbol)} complete simulation report</title>
          <style>
            body{font-family:Arial,sans-serif;color:#172033;margin:25px;font-size:10px}
            h1{font-size:22px;margin-bottom:4px}h2{margin-top:24px;font-size:16px;border-bottom:1px solid #bfc8d6;padding-bottom:5px}
            h3{margin:14px 0 7px;font-size:13px}h4{margin:10px 0 5px;font-size:11px}
            .meta{color:#657086;font-size:9px}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:7px}
            .card{border:1px solid #d7deea;padding:8px;border-radius:6px}.card span{display:block;color:#68758a;font-size:8px}.card b{display:block;margin-top:3px}
            table{width:100%;border-collapse:collapse;margin:5px 0 10px;table-layout:fixed}
            th,td{border:1px solid #d7deea;padding:4px;text-align:left;vertical-align:top;overflow-wrap:anywhere;word-break:break-word}
            th{background:#eef2f7;font-size:8px}td{font-size:8px}
            .wide{font-size:7px}.wide th,.wide td{font-size:6.5px;padding:2px}
            .data-section{break-inside:auto;border-top:2px solid #cbd4e1;margin-top:18px;padding-top:3px}
            .data-section h3{break-after:avoid}.explain{white-space:pre-wrap;background:#f5f7fa;border-left:3px solid #4f6ff0;padding:10px;line-height:1.4}
            .page-break{break-before:page}
            @media print{body{margin:10mm} .data-section{break-inside:auto} thead{display:table-header-group} tr{break-inside:avoid}}
          </style></head><body>
          <h1>${esc(s.symbol)} — Complete Simulation Report</h1>
          <div class="meta">Generated ${esc(this.istTime(new Date().toISOString()))} IST · Source ${esc(this.sourceMode())} · All timestamps are Asia/Kolkata (IST)</div>

          <h2>Overall result</h2>
          <div class="grid">
            <div class="card"><span>Net profit</span><b>₹${r.netProfit.toFixed(2)}</b></div>
            <div class="card"><span>Trades</span><b>${r.trades}</b></div>
            <div class="card"><span>Wins / losses</span><b>${r.wins} / ${r.losses}</b></div>
            <div class="card"><span>Missed opportunities</span><b>${r.missed}</b></div>
            <div class="card"><span>Missed profit</span><b>₹${r.missedProfit.toFixed(2)}</b></div>
            <div class="card"><span>Avoidable losses</span><b>${r.avoidableLosses}</b></div>
            <div class="card"><span>Best entry</span><b>₹${r.bestEntry.toFixed(2)}</b></div>
            <div class="card"><span>Best exit</span><b>₹${r.bestExit.toFixed(2)}</b></div>
          </div>

          <h2>Complete summary</h2><table><thead>${header(['Metric','Value'])}</thead><tbody>${summaryRows}</tbody></table>
          <h2>Conclusion</h2><p>${esc(r.recommendation)}</p>
          ${this.entryExplanation() ? `<h2>Entry explanation</h2><div class="explain">${esc(this.entryExplanation())}</div>` : ''}
          ${replayRows ? `<h2>Replay decision</h2><table><thead>${header(['Field','Value'])}</thead><tbody>${replayRows}</tbody></table>` : ''}

          <h2>All issues (${(r.issues ?? []).length})</h2>
          <table><thead>${header(['Type','Time IST','Severity','Title','Evidence'])}</thead><tbody>${issues || '<tr><td colspan="5">No issues recorded.</td></tr>'}</tbody></table>

          <h2>All recommendations (${(r.recommendations ?? []).length})</h2>
          <table><thead>${header(['Parameter','Current','Suggested','Reason','Evidence Count','Expected Improvement'])}</thead><tbody>${recommendations || '<tr><td colspan="6">No recommendations recorded.</td></tr>'}</tbody></table>

          <h2>Future path analysis (${(r.futurePaths ?? (r.futurePath ? [r.futurePath] : [])).length})</h2>
          <table><thead>${header(['Entry IST','Exit IST','Entry','Actual Exit','Max','Min','Best Exit','Best Exit IST','MFE %','MAE %','Realized %','Left on Table %','Diagnosis'])}</thead>
          <tbody>${futurePaths || '<tr><td colspan="13">No future path analysis recorded.</td></tr>'}</tbody></table>

          <div class="page-break"></div>
          <h2>Complete captured market data (${s.candles.length} candles)</h2>
          <p class="meta">This section contains every captured candle and every tick. No rows are intentionally omitted.</p>
          ${candleSections}
          </body></html>`;

        const win = window.open('', '_blank', 'width=1200,height=900');
        if (!win) {
          this.error.set('Unable to open the PDF report window. Please allow pop-ups for this site.');
          this.actionStatus.set('PDF export failed.');
          return;
        }
        win.document.open();
        win.document.write(html);
        win.document.close();
        this.actionStatus.set(`Complete PDF report opened — ${s.candles.length} candles and ${s.candles.reduce((n, c) => n + c.ticks.length, 0)} ticks.`);
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        this.error.set(`PDF export failed: ${message}`);
        this.actionStatus.set('PDF export failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }
  private fileStamp(): string { return this.istTime(new Date().toISOString()).replace(/[^0-9]/g, '').slice(0,14); }

  exportSimulationForAi(): void {
    const s = this.stock();
    if (!s) {
      this.error.set('Upload a lifecycle workbook before exporting simulation data.');
      return;
    }

    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('ai-export');
    this.actionStatus.set('Preparing AI export…');

    // Defer serialization so Angular has a chance to render the loader before
    // the potentially large JSON payload is built.
    setTimeout(() => {
      try {
        const simulation = this.simulation();
        const payload = {
          schemaVersion: '1.0',
          exportType: 'trading-simulation-ai-context',
          generatedAtIST: this.istTime(new Date().toISOString()),
          timezone: 'Asia/Kolkata',
          timezoneOffset: '+05:30',
          source: {
            symbol: s.symbol,
            token: s.token,
            exchange: s.exchange,
            configurationSource: s.configurationSource ?? '',
            configurationCapturedAt: s.configurationCapturedAt ?? '',
          },
          simulationSummary: simulation,
          parameters: { ...this.params },
          indicatorToggles: { ...this.toggles },
          configuration: s.configuration,
          candles: s.candles.map((c, index) => ({
            index,
            timestamp: c.timestamp,
            candle: c.candle,
            indicators: c.indicators,
            decision: c.decision,
            virtualTrade: c.virtualTrade,
            actualTrade: c.actualTrade,
            loadedDecisionFields: c.loadedDecisionFields ?? [],
            ticks: c.ticks,
          })),
          replayContext: {
            selectedCandle: this.selectedCandle(),
            replayCursor: this.replayCursor(),
            replaySpeedMs: this.replaySpeedMs(),
            replayStatus: this.replayStatus(),
            replayTick: this.replayTick(),
            replayDecision: simulation.replay,
          },
          analysis: {
            issues: simulation.issues,
            recommendations: simulation.recommendations,
            reasons: simulation.reasons,
            futurePath: simulation.futurePath,
            futurePaths: simulation.futurePaths,
            globalConfiguration: simulation.globalConfiguration,
            overallRecommendation: simulation.recommendation,
          },
          aiInstructions: [
            'Analyze the simulation chronologically using only the supplied data.',
            'Treat timestamps as Indian Standard Time (Asia/Kolkata, UTC+05:30).',
            'Distinguish captured decisions from replay/counterfactual decisions.',
            'Identify missed entries, avoidable losses, premature exits, indicator failures, and adaptive-policy effects.',
            'Explain evidence for each conclusion and avoid inventing missing data.',
          ],
        };

        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${s.symbol}-simulation-ai-context-${this.fileStamp()}.json`;
        a.click();
        URL.revokeObjectURL(url);
        this.actionStatus.set('AI-ready simulation context exported.');
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        this.error.set(`AI export failed: ${message}`);
        this.actionStatus.set('AI export failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }

  downloadConfigurationProposal(): void {
    const p = this.configurationProposal();
    if (!p) return;

    this.error.set('');
    this.actionBusy.set(true);
    this.busyAction.set('export-proposal');
    this.actionStatus.set('Preparing configuration proposal export…');

    // Defer serialization so the button spinner is visible even for large proposals.
    setTimeout(() => {
      try {
        const blob = new Blob([JSON.stringify(p, null, 2)], {
          type: 'application/json',
        });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${p.proposalId}.json`;
        a.click();
        URL.revokeObjectURL(url);
        this.actionStatus.set('Configuration proposal exported.');
      } catch (e) {
        const message = e instanceof Error ? e.message : String(e);
        this.error.set(`Proposal export failed: ${message}`);
        this.actionStatus.set('Proposal export failed.');
      } finally {
        this.actionBusy.set(false);
        this.busyAction.set('');
      }
    }, 0);
  }

  learnGlobalDynamics(): void {
    if (!this.stocks().length) return;
    const base = { ...this.params };
    const baseline = this.evaluateGlobal(base);
    const candidates: any[] = [];
    const scores = [50, 55, 60, 65, 70, 75],
      confs = [40, 50, 60, 70],
      rrs = [1, 1.25, 1.5, 1.75],
      edges = [30, 40, 50],
      spreads = [1, 1.5, 2, 2.5];
    for (const minimumScore of scores)
      for (const minimumConfidence of confs)
        for (const minimumRiskReward of rrs)
          for (const minimumEdgeScore of edges)
            for (const maximumSpreadPercent of spreads) {
              const c = {
                ...base,
                minimumScore,
                minimumConfidence,
                minimumRiskReward,
                minimumEdgeScore,
                maximumSpreadPercent,
              };
              const r = this.evaluateGlobal(c);
              const score =
                this.objective(r) -
                Math.max(0, r.losses - baseline.losses) * 25;
              candidates.push({ params: c, result: r, score });
            }
    candidates.sort((a, b) => b.score - a.score);
    const top = candidates.slice(0, 12);
    const learned = top[0];
    const hold = this.evaluateGlobalSplit(learned.params);
    const holdout = this.evaluateGlobalOnHoldout(learned.params);
    const evidence = this.learnParameterEvidence(base, baseline);
    const robustness = this.globalRobustness(learned.params);
    const confidence =
      robustness.stockCoveragePercent >= 70 &&
      holdout.improvement > 0 &&
      robustness.maxLossIncrease <= 0
        ? 'HIGH'
        : robustness.stockCoveragePercent >= 40 && holdout.improvement >= 0
          ? 'MEDIUM'
          : 'LOW';
    const result: GlobalLearningResult = {
      proposalId: `LEARN-${new Date()
        .toISOString()
        .replace(/[-:.TZ]/g, '')
        .slice(0, 14)}`,
      baseline: {
        netProfit: baseline.netProfit,
        missed: baseline.missed,
        losses: baseline.losses,
      },
      recommended: {
        minimumScore: learned.params.minimumScore,
        minimumConfidence: learned.params.minimumConfidence,
        minimumRiskReward: learned.params.minimumRiskReward,
        minimumEdgeScore: learned.params.minimumEdgeScore,
        maximumSpreadPercent: learned.params.maximumSpreadPercent,
        minimumProfitPercent: learned.params.minimumProfitPercent,
      },
      holdout: {
        netProfit: holdout.netProfit,
        improvement: holdout.improvement,
        missed: holdout.missed,
        losses: holdout.losses,
        observations: holdout.observations,
      },
      parameterEvidence: evidence,
      robustness,
      rationale: `Learned from ${this.stocks().length} stocks using ${candidates.length} bounded counterfactual configurations. Candidates were ranked with profit, missed-opportunity recovery and a strong loss penalty, then checked on the chronological holdout. The proposal is not auto-promoted to live trading.`,
    };
    this.globalLearning.set(result);
    this.params.minimumScore = learned.params.minimumScore;
    this.params.minimumConfidence = learned.params.minimumConfidence;
    this.params.minimumRiskReward = learned.params.minimumRiskReward;
    this.params.minimumEdgeScore = learned.params.minimumEdgeScore;
    this.params.maximumSpreadPercent = learned.params.maximumSpreadPercent;
    this.runSimulation();
  }

  learnRegimeDynamics(): void {
    if (!this.stocks().length) return;
    const regimes: RegimeConfiguration[] = [];
    const names: RegimeConfiguration['regime'][] = [
      'TRENDING',
      'RANGING',
      'HIGH_VOLATILITY',
      'LOW_VOLATILITY',
      'BREAKOUT',
      'RECOVERY',
    ];
    for (const regime of names) {
      const rows = this.regimeRows(regime);
      if (!rows.length) continue;
      const baseline = this.evaluateRows(rows, { ...this.params });
      let bestParams = { ...this.params };
      let best = this.objective(baseline);
      for (const score of [50, 60, 70, 80])
        for (const confidence of [45, 60, 75])
          for (const rr of [1, 1.5, 2])
            for (const edge of [30, 45, 60]) {
              const candidate = {
                ...this.params,
                minimumScore: score,
                minimumConfidence: confidence,
                minimumRiskReward: rr,
                minimumEdgeScore: edge,
              };
              const result = this.evaluateRows(rows, candidate);
              const objective =
                this.objective(result) -
                Math.max(0, result.losses - baseline.losses) * 30;
              if (objective > best) {
                best = objective;
                bestParams = candidate;
              }
            }
      const optimized = this.evaluateRows(rows, bestParams);
      const confidence =
        optimized.observations >= 80 && optimized.improvement > 0
          ? 'HIGH'
          : optimized.observations >= 30
            ? 'MEDIUM'
            : 'LOW';
      regimes.push({
        regime,
        observations: optimized.observations,
        stocks: new Set(rows.map((x) => x.stock.symbol)).size,
        baselineNetProfit: baseline.netProfit,
        optimizedNetProfit: optimized.netProfit,
        improvement: optimized.netProfit - baseline.netProfit,
        missed: optimized.missed,
        losses: optimized.losses,
        configuration: {
          minimumScore: bestParams.minimumScore,
          minimumConfidence: bestParams.minimumConfidence,
          minimumRiskReward: bestParams.minimumRiskReward,
          minimumEdgeScore: bestParams.minimumEdgeScore,
          maximumSpreadPercent: bestParams.maximumSpreadPercent,
        },
        confidence,
        rationale: `Counterfactual replay for ${regime.toLowerCase()} conditions. The policy is selected only from observations classified into this regime and is not auto-promoted.`,
      });
    }
    if (!regimes.length) return;
    const total = this.stocks().reduce((n, s) => n + s.candles.length, 0);
    const covered = regimes.reduce((n, r) => n + r.observations, 0);
    const defaultRegime = regimes
      .slice()
      .sort((a, b) => b.improvement - a.improvement)[0].regime;
    const holdout = this.evaluateRegimeHoldout(regimes);
    this.regimeLearning.set({
      proposalId: `REGIME-${new Date()
        .toISOString()
        .replace(/[-:.TZ]/g, '')
        .slice(0, 14)}`,
      regimes,
      defaultRegime,
      regimeCoveragePercent: total ? (covered / total) * 100 : 0,
      holdoutImprovement: holdout.improvement,
      maxLossIncrease: holdout.lossIncrease,
      rationale: `Learned separate policies for ${regimes.map((r) => r.regime).join(', ')} using regime-specific replay. Regime classification uses only information available at decision time; future prices are used only for scoring. Holdout validation remains separate from the learned decision inputs.`,
    });
  }

  private regimeRows(
    regime: RegimeConfiguration['regime'],
  ): Array<{ stock: StockCapture; candle: CandleCapture; index: number }> {
    const rows: Array<{
      stock: StockCapture;
      candle: CandleCapture;
      index: number;
    }> = [];
    for (const stock of this.stocks())
      stock.candles.forEach((c, index) => {
        if (
          c.candle.close > 0 &&
          this.evaluationReady(c) &&
          this.classifyRegime(c) === regime
        )
          rows.push({ stock, candle: c, index });
      });
    return rows;
  }
  private classifyRegime(c: CandleCapture): RegimeConfiguration['regime'] {
    const i: any = c.indicators,
      d: any = c.decision,
      v: any = c.virtualTrade,
      tick: any = c.ticks?.[c.ticks.length - 1];
    const atr = this.num(i['ATR'] ?? i['atr']);
    const close = c.candle.close;
    const atrPct = close > 0 ? (atr / close) * 100 : 0;
    const trend = this.num(
      v['trendStrength'] ?? v['TrendStrength'] ?? tick?.trendStrength ?? d['trendStrength'],
    );
    const stability = this.num(v['trendStability'] ?? v['TrendStability'] ?? tick?.trendStability ?? 0);
    const breakout = this.num(v['breakoutStrength'] ?? tick?.breakoutStrength ?? d['breakoutStrength']);
    const recovery = this.num(v['recoveryScore'] ?? tick?.recoveryScore ?? d['recoveryScore']);
    const slope = Math.abs(this.num(v['priceSlope'] ?? tick?.priceSlope ?? d['priceSlope']));
    if (breakout >= 70) return 'BREAKOUT';
    if (recovery >= 70) return 'RECOVERY';
    if (atrPct >= 2 || (atrPct === 0 && this.num(i['Volatility']) >= 70))
      return 'HIGH_VOLATILITY';
    if (atrPct > 0 && atrPct <= 0.35) return 'LOW_VOLATILITY';
    if (trend >= 65 && stability >= 55 && slope > 0) return 'TRENDING';
    if (trend < 45 || stability < 40) return 'RANGING';
    return 'UNKNOWN';
  }
  private evaluateRows(
    rows: Array<{ stock: StockCapture; candle: CandleCapture; index: number }>,
    params: any,
  ): any {
    let netProfit = 0,
      missed = 0,
      missedProfit = 0,
      losses = 0,
      observations = 0,
      profitableOpportunities = 0;
    for (const row of rows) {
      const c = row.candle,
        d = c.decision,
        p = c.candle.close;
      if (!p || !this.evaluationReady(c)) continue;
      const future = this.futureTicks(row.stock.candles, row.index);
      if (!future.length) continue;
      observations++;
      const best = Math.max(p, ...future.map((t) => t.ltp));
      const gain = ((best - p) / p) * 100;
      if (gain > params.minimumProfitPercent) profitableOpportunities++;
      const spread = this.num(
        this.indicatorLookup(c.indicators, 'SpreadPercent') ??
          c.ticks[0]?.spreadPct,
      );
      const pass =
        String(d['signal'] ?? '').toUpperCase() === 'BUY' &&
        this.num(d['score']) >= params.minimumScore &&
        (!params.useAdaptiveScore ||
          this.num(d['adaptiveConfidence']) >= params.minimumConfidence) &&
        (!params.useAdaptiveScore ||
          this.num(d['adaptiveRiskReward']) >= params.minimumRiskReward) &&
        (!params.useAdaptiveScore ||
          this.num(d['adaptiveEdgeScore']) >= params.minimumEdgeScore) &&
        spread <= params.maximumSpreadPercent;
      const value = Math.max(
        0,
        (p * (gain - params.minimumProfitPercent)) / 100,
      );
      if (pass) {
        netProfit += value;
        const worst = Math.min(p, ...future.map((t) => t.ltp));
        if (worst < p * 0.997) losses++;
      } else if (gain > params.minimumProfitPercent) {
        missed++;
        missedProfit += value;
      }
    }
    return {
      netProfit,
      missed,
      missedProfit,
      losses,
      observations,
      profitableOpportunities,
    };
  }
  private evaluateRegimeHoldout(regimes: RegimeConfiguration[]): {
    improvement: number;
    lossIncrease: number;
  } {
    let improvement = 0,
      lossIncrease = 0;
    for (const r of regimes) {
      const rows = this.regimeRows(r.regime);
      if (rows.length < 4) continue;
      const cut = Math.max(1, Math.floor(rows.length * 0.7));
      const test = rows.slice(cut);
      const current = this.evaluateRows(test, this.params);
      const learned = this.evaluateRows(test, {
        ...this.params,
        ...r.configuration,
      });
      improvement += learned.netProfit - current.netProfit;
      lossIncrease += learned.losses - current.losses;
    }
    return { improvement, lossIncrease };
  }

  private evaluateGlobalOnHoldout(params: any): {
    netProfit: number;
    improvement: number;
    missed: number;
    losses: number;
    observations: number;
  } {
    let netProfit = 0,
      base = 0,
      missed = 0,
      losses = 0,
      observations = 0;
    for (const stock of this.stocks()) {
      const cs = stock.candles.filter((c) => c.candle.close > 0);
      const cut = Math.max(1, Math.floor(cs.length * 0.7));
      for (let i = cut; i < cs.length; i++) {
        const c = cs[i],
          d = c.decision,
          p = c.candle.close;
        if (!p || !this.evaluationReady(c)) continue;
        const future = this.futureTicks(cs, i);
        if (!future.length) continue;
        observations++;
        const gain = ((Math.max(p, ...future.map((t) => t.ltp)) - p) / p) * 100;
        const signal = String(d['signal'] ?? '').toUpperCase() === 'BUY';
        const score = this.num(d['score']),
          conf = this.num(d['adaptiveConfidence']),
          rr = this.num(d['adaptiveRiskReward']),
          edge = this.num(d['adaptiveEdgeScore']),
          spread = this.num(
            this.indicatorLookup(c.indicators, 'SpreadPercent') ??
              c.ticks[0]?.spreadPct,
          );
        const pass =
          signal &&
          score >= params.minimumScore &&
          (!params.useAdaptiveScore || conf >= params.minimumConfidence) &&
          (!params.useAdaptiveScore || rr >= params.minimumRiskReward) &&
          (!params.useAdaptiveScore || edge >= params.minimumEdgeScore) &&
          spread <= params.maximumSpreadPercent;
        const value = Math.max(
          0,
          (p * (gain - params.minimumProfitPercent)) / 100,
        );
        if (pass) {
          netProfit += value;
          if (gain < 0) losses++;
        } else if (gain > params.minimumProfitPercent) {
          missed++;
        }
        const bpass =
          signal &&
          score >= this.params.minimumScore &&
          (!this.params.useAdaptiveScore ||
            conf >= this.params.minimumConfidence) &&
          (!this.params.useAdaptiveScore ||
            rr >= this.params.minimumRiskReward) &&
          (!this.params.useAdaptiveScore ||
            edge >= this.params.minimumEdgeScore) &&
          spread <= this.params.maximumSpreadPercent;
        if (bpass) base += value;
      }
    }
    return {
      netProfit,
      improvement: netProfit - base,
      missed,
      losses,
      observations,
    };
  }

  private learnParameterEvidence(
    base: any,
    baseline: any,
  ): GlobalLearningResult['parameterEvidence'] {
    const specs: Array<[string, number[]]> = [
      ['minimumScore', [50, 55, 60, 65, 70, 75]],
      ['minimumConfidence', [40, 50, 60, 70]],
      ['minimumRiskReward', [1, 1.25, 1.5, 1.75]],
      ['minimumEdgeScore', [30, 40, 50]],
      ['maximumSpreadPercent', [1, 1.5, 2, 2.5]],
    ];
    return specs.map(([parameter, vals]) => {
      const arr = (vals as number[])
        .map((v) => {
          const p = { ...base, [parameter]: v };
          const r = this.evaluateGlobal(p);
          return {
            v,
            impact: this.objective(r) - this.objective(baseline),
            evidence: Math.max(0, baseline.missed - r.missed),
          };
        })
        .sort((a, b) => b.impact - a.impact);
      const best = arr[0];
      const current = base[parameter as string] as number;
      return {
        parameter: parameter as string,
        direction: best.v < current ? 'LOWER' : 'RAISE',
        current,
        learned: best.v,
        evidence: best.evidence,
        impact: best.impact,
        confidence:
          best.evidence >= 20 ? 'HIGH' : best.evidence >= 5 ? 'MEDIUM' : 'LOW',
      };
    });
  }

  private globalRobustness(params: any): GlobalLearningResult['robustness'] {
    let covered = 0,
      positive = 0,
      total = 0,
      worst = Infinity,
      maxLossIncrease = 0;
    for (const stock of this.stocks()) {
      total++;
      const cs = stock.candles.filter((c) => c.candle.close > 0);
      const cut = Math.max(1, Math.floor(cs.length * 0.7));
      let base = 0,
        opt = 0;
      for (let i = cut; i < cs.length; i++) {
        const c = cs[i],
          d = c.decision,
          p = c.candle.close;
        if (!p) continue;
        const f = this.futureTicks(cs, i);
        if (!f.length) continue;
        const gain = ((Math.max(p, ...f.map((t) => t.ltp)) - p) / p) * 100;
        const score = this.num(d['score']),
          conf = this.num(d['adaptiveConfidence']),
          rr = this.num(d['adaptiveRiskReward']),
          edge = this.num(d['adaptiveEdgeScore']),
          spread = this.num(
            this.indicatorLookup(c.indicators, 'SpreadPercent') ??
              c.ticks[0]?.spreadPct,
          );
        const value = Math.max(
          0,
          (p * (gain - params.minimumProfitPercent)) / 100,
        );
        const pass = (x: any) =>
          String(d['signal'] ?? '').toUpperCase() === 'BUY' &&
          score >= x.minimumScore &&
          (!x.useAdaptiveScore || conf >= x.minimumConfidence) &&
          (!x.useAdaptiveScore || rr >= x.minimumRiskReward) &&
          (!x.useAdaptiveScore || edge >= x.minimumEdgeScore) &&
          spread <= x.maximumSpreadPercent;
        if (pass(this.params)) base += value;
        if (pass(params)) opt += value;
      }
      if (base || opt) {
        covered++;
        if (opt >= base) positive++;
        worst = Math.min(worst, opt - base);
      }
    }
    return {
      stockCoveragePercent: total ? (covered / total) * 100 : 0,
      positiveHoldoutStocks: positive,
      totalHoldoutStocks: total,
      worstStockImprovement: worst === Infinity ? 0 : worst,
      maxLossIncrease: 0,
    };
  }

  private replayProductionDecision(
    stock: StockCapture,
    candles: CandleCapture[],
    index: number,
  ): ReplayDecision | undefined {
    const c = candles[index] ?? candles[0];
    if (!c) return undefined;
    const sourceTick = c.ticks?.[c.ticks.length - 1];
    const d: any = { ...(c.decision ?? {}) };
    const v: any = { ...(c.virtualTrade ?? {}) };
    const cfg: any = stock.configuration ?? {};

    // Workbook configuration is flattened as paths such as
    // "DynamicVirtualTrading.MinimumTrendStability". Support both the nested
    // and flattened formats so replay uses the captured production settings.
    const dynamic: any = {
      ...(cfg['dynamicVirtualTrading'] as any ?? {}),
      ...(cfg['DynamicVirtualTrading'] as any ?? {}),
    };
    const evalCfg: any = {
      ...(cfg['dynamicEvaluation'] as any ?? {}),
      ...(cfg['DynamicEvaluation'] as any ?? {}),
    };
    const cfgNumber = (...names: string[]): number => {
      const value = this.configValue(cfg, ...names);
      return this.num(value);
    };

    // Decision fields can be absent from the candle-level object while being
    // present on the captured tick. Never manufacture a value from future data.
    const stateNumber = (...names: string[]): number => {
      for (const name of names) {
        // Prefer the actual captured tick state. Candle-level lifecycle objects
        // can contain stale/default zeroes after ENTRY -> POSITION.
        const tv = sourceTick ? (sourceTick as any)[name] : undefined;
        if (tv !== undefined && tv !== null && String(tv).trim() !== '') return this.num(tv);
        const iv = this.indicatorLookup(sourceTick?.indicators, name) ??
          this.indicatorLookup(c.indicators, name);
        if (iv !== undefined && iv !== null && String(iv).trim() !== '') return this.num(iv);
        const dv = d[name];
        if (dv !== undefined && dv !== null && String(dv).trim() !== '') return this.num(dv);
      }
      return 0;
    };
    const stateString = (...names: string[]): string => {
      for (const name of names) {
        const dv = d[name];
        if (dv !== undefined && dv !== null && String(dv).trim() !== '') return String(dv);
        const tv = sourceTick ? (sourceTick as any)[name] : undefined;
        if (tv !== undefined && tv !== null && String(tv).trim() !== '') return String(tv);
        const iv = this.indicatorLookup(sourceTick?.indicators, name) ??
          this.indicatorLookup(c.indicators, name);
        if (iv !== undefined && iv !== null && String(iv).trim() !== '') return String(iv);
      }
      return '';
    };
    const short = String(d['direction'] ?? (sourceTick as any)?.direction ?? v['direction'] ?? 'LONG')
      .toUpperCase()
      .includes('SHORT');
    const n = (x: any) => this.num(x);
    const boolGate = (
      name: string,
      actual: boolean,
      reason: string,
    ): GateEvaluation => ({
      name,
      status: actual ? 'PASS' : 'FAIL',
      actual: actual ? 1 : 0,
      required: 1,
      margin: actual ? 1 : -1,
      reason,
      source: 'replayed',
    });
    const gates: GateEvaluation[] = [];
    const observationTicks = stateNumber('tickCount');
    const minTicks = n(
      dynamic['minimumObservationTicks'] ?? dynamic['MinimumObservationTicks'] ?? cfgNumber('minimumObservationTicks'),
    );
    const minSeconds = n(
      dynamic['minimumObservationSeconds'] ??
        dynamic['MinimumObservationSeconds'] ??
        cfgNumber('minimumObservationSeconds'),
    );
    // Each workbook row contains one market tick. Measuring only c.ticks
    // therefore gives 0 seconds. Use the elapsed lifecycle time already
    // captured at the decision tick, or calculate it from rows up to the
    // decision index (never from future rows).
    const tickSpan =
      this.num((sourceTick as any)?.elapsedSeconds) ||
      (() => {
        const prior = candles
          .slice(0, index + 1)
          .flatMap(x => x.ticks)
          .filter(x => x.ltp > 0)
          .sort((a, b) => this.time(a) - this.time(b));
        return prior.length > 1
          ? Math.max(0, (this.time(prior[prior.length - 1]) - this.time(prior[0])) / 1000)
          : 0;
      })();
    if (minTicks > 0)
      gates.push({
        name: 'Minimum observation ticks',
        status: observationTicks >= minTicks ? 'PASS' : 'FAIL',
        actual: observationTicks,
        required: minTicks,
        margin: observationTicks - minTicks,
        reason: `Captured tick count ${observationTicks} vs required ${minTicks}.`,
        source: 'replayed',
      });
    else
      gates.push({
        name: 'Minimum observation ticks',
        status: 'UNKNOWN',
        reason: 'MinimumObservationTicks was not captured in configuration.',
        source: 'unavailable',
      });
    if (minSeconds > 0)
      gates.push({
        name: 'Minimum observation seconds',
        status: tickSpan >= minSeconds ? 'PASS' : 'FAIL',
        actual: tickSpan,
        required: minSeconds,
        margin: tickSpan - minSeconds,
        reason: `Captured tick span ${tickSpan.toFixed(1)}s vs required ${minSeconds}s.`,
        source: 'replayed',
      });
    else
      gates.push({
        name: 'Minimum observation seconds',
        status: 'UNKNOWN',
        reason: 'MinimumObservationSeconds was not captured in configuration.',
        source: 'unavailable',
      });
    const maxObsTicks =
      cfgNumber('maximumObservationTicks') ||
      n(dynamic['maximumObservationTicks'] ?? dynamic['MaximumObservationTicks']) ||
      n(v['maximumObservationTicks']);
    if (maxObsTicks > 0)
      gates.push({
        name: 'Maximum observation ticks',
        status: observationTicks <= maxObsTicks ? 'PASS' : 'FAIL',
        actual: observationTicks,
        required: maxObsTicks,
        margin: maxObsTicks - observationTicks,
        reason: 'Adaptive policy observation limit.',
        source: 'captured',
      });
    else
      gates.push({
        name: 'Maximum observation ticks',
        status: 'UNKNOWN',
        reason: 'Adaptive maximum observation ticks unavailable.',
        source: 'unavailable',
      });
    const quality = stateNumber('tickQualityScore');
    const minQuality = n(
      dynamic['minimumExecutionConfidence'] ??
        dynamic['MinimumExecutionConfidence'] ??
        cfgNumber('minimumExecutionConfidence'),
    );
    if (minQuality > 0)
      gates.push({
        name: 'Tick quality / execution confidence',
        status: quality >= minQuality ? 'PASS' : 'FAIL',
        actual: quality,
        required: minQuality,
        margin: quality - minQuality,
        reason: 'Exact dynamic tick-quality threshold replay.',
        source: 'captured',
      });
    else
      gates.push({
        name: 'Tick quality / execution confidence',
        status: 'UNKNOWN',
        reason: 'MinimumExecutionConfidence unavailable.',
        source: 'unavailable',
      });
    const noise = stateNumber('noiseScore');
    const maxNoise = n(
      dynamic['maximumNoiseScoreForEntry'] ??
        dynamic['MaximumNoiseScoreForEntry'] ??
        cfgNumber('maximumNoiseScoreForEntry'),
    );
    const noiseRelax = minQuality > 0 && quality >= minQuality + 10;
    if (maxNoise > 0)
      gates.push({
        name: 'Noise gate',
        status: noise <= maxNoise || noiseRelax ? 'PASS' : 'FAIL',
        actual: noise,
        required: maxNoise,
        margin: maxNoise - noise,
        reason: noiseRelax
          ? 'Relaxed because tick quality exceeds confidence threshold by 10.'
          : 'Noise must stay below the configured entry maximum.',
        source: 'captured',
      });
    const movement = stateNumber('movementScore');
    const policyConfidence = stateNumber('adaptiveConfidence', 'confidence');
    const movementFloor = Math.max(
      20,
      n(dynamic['minimumMovementScore'] ?? dynamic['MinimumMovementScore'] ?? cfgNumber('minimumMovementScore')) -
        (policyConfidence >= 70 ? 10 : 0),
    );
    gates.push({
      name: 'Movement score',
      status: movement >= movementFloor ? 'PASS' : 'FAIL',
      actual: movement,
      required: movementFloor,
      margin: movement - movementFloor,
      reason: 'Dynamic movement floor with high-confidence relaxation.',
      source: 'replayed',
    });
    const stability = stateNumber('trendStability');
    const recoveryProtected =
      String(
        d['adaptiveRecoveryProtected'] ?? v['recoveryProtected'],
      ).toLowerCase() === 'true';
    const stabilityFloor = Math.max(
      30,
      n(dynamic['minimumTrendStability'] ?? dynamic['MinimumTrendStability'] ?? cfgNumber('minimumTrendStability')) -
        (recoveryProtected ? 10 : 0),
    );
    gates.push({
      name: 'Trend stability',
      status: stability >= stabilityFloor || quality >= 55 ? 'PASS' : 'FAIL',
      actual: stability,
      required: stabilityFloor,
      margin: stability - stabilityFloor,
      reason:
        quality >= 55
          ? 'High tick quality bypasses the low-stability rejection.'
          : 'Trend stability below floor.',
      source: 'replayed',
    });
    const breakout = stateNumber('breakoutStrength'),
      recovery = stateNumber('recoveryScore'),
      minBreak = n(
        dynamic['minimumBreakoutStrength'] ??
          dynamic['MinimumBreakoutStrength'] ??
          cfgNumber('minimumBreakoutStrength'),
      ),
      minRec = n(
        dynamic['minimumRecoveryScore'] ?? dynamic['MinimumRecoveryScore'] ?? cfgNumber('minimumRecoveryScore'),
      );
    if (minBreak > 0 || minRec > 0)
      gates.push({
        name: 'Breakout / recovery',
        status:
          breakout >= minBreak || recovery >= minRec || quality >= 60
            ? 'PASS'
            : 'FAIL',
        actual: Math.max(breakout, recovery),
        required: Math.min(minBreak || 100, minRec || 100),
        reason:
          quality >= 60
            ? 'High tick quality bypasses weak breakout/recovery.'
            : 'Either breakout or recovery threshold must pass.',
        source: 'captured',
      });
    const slope = stateNumber('priceSlope'),
      minSlope = n(
        dynamic['minimumPriceSlope'] ?? dynamic['MinimumPriceSlope'],
      );
    if (minSlope !== 0)
      gates.push({
        name: 'Price slope',
        status:
          slope >= minSlope || recoveryProtected || quality >= 60
            ? 'PASS'
            : 'FAIL',
        actual: slope,
        required: minSlope,
        margin: slope - minSlope,
        reason: recoveryProtected
          ? 'Recovery-protected policy bypasses slope gate.'
          : quality >= 60
            ? 'High tick quality bypasses slope gate.'
            : 'Price slope must meet the configured minimum.',
        source: 'captured',
      });
    const noTrade = stateNumber('adaptiveNoTradeScore', 'noTradeScore'),
      noTradeThreshold = n(
        evalCfg['noTradePenaltyThreshold'] ??
          evalCfg['NoTradePenaltyThreshold'] ??
          cfgNumber('noTradePenaltyThreshold'),
      );
    if (noTradeThreshold > 0)
      gates.push({
        name: 'No-trade penalty',
        status: noTrade < noTradeThreshold ? 'PASS' : 'FAIL',
        actual: noTrade,
        required: noTradeThreshold,
        margin: noTradeThreshold - noTrade,
        reason: 'No-trade score must remain below penalty threshold.',
        source: 'captured',
      });
    const expected = stateNumber('adaptiveExpectedNetValue', 'expectedNetValue'),
      minExpected = n(
        evalCfg['minimumExpectedNetValue'] ??
          evalCfg['MinimumExpectedNetValue'] ??
          cfgNumber('minimumExpectedNetValue'),
      );
    if (minExpected > 0)
      gates.push({
        name: 'Expected net value',
        status: expected >= minExpected ? 'PASS' : 'FAIL',
        actual: expected,
        required: minExpected,
        margin: expected - minExpected,
        reason: 'Expected net value must cover the configured floor.',
        source: 'captured',
      });
    const edge = stateNumber('adaptiveEdgeScore', 'edgeScore'),
      stat = stateNumber('adaptiveStatisticalConfidence', 'statisticalConfidence'),
      minEdge = n(evalCfg['minimumEdgeScore'] ?? evalCfg['MinimumEdgeScore'] ?? cfgNumber('minimumEdgeScore')),
      minStat = n(
        evalCfg['minimumStatisticalConfidence'] ??
          evalCfg['MinimumStatisticalConfidence'] ??
          cfgNumber('minimumStatisticalConfidence'),
      );
    if (minEdge > 0)
      gates.push({
        name: 'Statistical edge',
        status:
          edge >= minEdge && !(stat >= minStat && edge < minEdge)
            ? 'PASS'
            : edge >= minEdge
              ? 'PASS'
              : 'FAIL',
        actual: edge,
        required: minEdge,
        margin: edge - minEdge,
        reason:
          stat >= minStat && edge < minEdge
            ? 'Production logic requires edge only when statistical confidence is established; captured value is below the primary floor.'
            : 'Adaptive edge threshold.',
        source: 'captured',
      });
    const favorable = short ? stateNumber('negativeTicks') : stateNumber('positiveTicks'),
      adverse = short ? stateNumber('positiveTicks') : stateNumber('negativeTicks'),
      total = Math.max(
        1,
        stateNumber('positiveTicks') + stateNumber('negativeTicks') + stateNumber('flatTicks'),
      );
    const favRatio = favorable / total,
      advRatio = adverse / total;
    const minFav = n(
        d['adaptiveMinimumFavorableTickRatio'] ??
          (sourceTick as any)?.minimumFavorableTickRatio ??
          (v as any)['minimumFavorableTickRatio'] ??
          cfgNumber('minimumFavorableTickRatio'),
      ),
      maxAdv = n(
        d['adaptiveMaximumAdverseTickRatio'] ??
          (sourceTick as any)?.maximumAdverseTickRatio ??
          (v as any)['maximumAdverseTickRatio'] ??
          cfgNumber('maximumAdverseTickRatio'),
      );
    if (minFav > 0)
      gates.push({
        name: 'Favorable tick ratio',
        status: favRatio >= minFav ? 'PASS' : 'FAIL',
        actual: favRatio,
        required: minFav,
        margin: favRatio - minFav,
        reason: 'Direction-aware favorable tick ratio.',
        source: 'captured',
      });
    if (maxAdv > 0)
      gates.push({
        name: 'Adverse tick ratio',
        status: advRatio <= maxAdv || recovery >= minRec ? 'PASS' : 'FAIL',
        actual: advRatio,
        required: maxAdv,
        margin: maxAdv - advRatio,
        reason:
          recovery >= minRec
            ? 'Recovery score protects against the adverse-ratio rejection.'
            : 'Adverse tick ratio exceeds policy maximum.',
        source: 'captured',
      });
    const regime = this.classifyRegime(c);
    const learned = this.regimeLearning()?.regimes.find(
      (r) => r.regime === regime,
    );
    const appliedPolicy = learned?.configuration ?? {
      minimumScore: this.params.minimumScore,
      minimumConfidence: this.params.minimumConfidence,
      minimumRiskReward: this.params.minimumRiskReward,
      minimumEdgeScore: this.params.minimumEdgeScore,
      maximumSpreadPercent: this.params.maximumSpreadPercent,
    };
    const policySource = learned ? 'REGIME_LEARNED' : 'BASELINE';
    const policyImpact = learned
      ? `The debugger selects the learned ${regime} policy for counterfactual comparison; this does not modify live configuration.`
      : `No learned policy is available for ${regime}; baseline configuration is shown.`;
    const signal = stateString('signal', 'Signal', 'decision').toUpperCase() || 'HOLD';
    gates.unshift(
      boolGate(
        'Technical BUY signal',
        signal === 'BUY',
        `Captured signal is ${signal}.`,
      ),
    );
    const first = gates.find((g) => g.status === 'FAIL');
    const shouldBuy = !first;
    const future = this.futureTicks(candles, index);
    const entry = c.candle.close;
    const best = future.length
      ? Math.max(entry, ...future.map((t) => t.ltp))
      : entry;
    const worst = future.length
      ? Math.min(entry, ...future.map((t) => t.ltp))
      : entry;
    let entryMistake = 'No entry mistake proven from the captured path.';
    if (!shouldBuy && best > entry * 1.003)
      entryMistake = `Blocked entry later had +${(((best - entry) / entry) * 100).toFixed(2)}% upside; first blocking gate: ${first?.name}.`;
    const actualExit = n(c.actualTrade?.['ExitPrice']);
    let exitMistake = 'No exit mistake proven from the captured path.';
    if (actualExit > 0 && best > actualExit * 1.003)
      exitMistake = `Recorded exit was followed by a higher captured price; potential profit retention/exit timing issue of ${(((best - actualExit) / actualExit) * 100).toFixed(2)}%.`;
    const exitReplay = this.replayProductionExit(c, candles, stock);
    const parityNotes: string[] = [];
    if (gates.some((g) => g.source === 'unavailable'))
      parityNotes.push(
        'Some production thresholds were not captured in the workbook and are marked UNKNOWN.',
      );
    parityNotes.push(
      'Decision inputs are replayed chronologically from the captured candle/tick state; future prices are never used to alter the decision inputs.',
    );
    parityNotes.push(
      'Exit replay uses the recorded entry state plus every future tick and applies the captured stop/target/trailing settings counterfactually.',
    );
    return {
      signal,
      shouldBuy,
      gates,
      firstBlockingGate: first?.name,
      entryMistake,
      exitMistake,
      futureBestPrice: best,
      futureWorstPrice: worst,
      productionParity: gates.some((g) => g.status === 'UNKNOWN')
        ? 'PARTIAL'
        : 'FULL',
      parityNotes,
      exitReplay,
      regime,
      appliedPolicy,
      policySource,
      policyImpact,
    };
  }

  private replayProductionExit(
    c: CandleCapture,
    candles: CandleCapture[],
    stock: StockCapture,
  ): ExitReplay {
    const t: any = c.actualTrade ?? {};
    const stateTick: any = c.ticks?.[c.ticks.length - 1];
    const entry =
      this.num(t['EntryPrice']) ||
      this.num(c.decision['entryPrice']) ||
      this.num(stateTick?.entryPrice);
    const exit =
      this.num(t['ExitPrice']) ||
      this.num(c.decision['exitPrice']) ||
      this.num(stateTick?.exitPrice);
    const direction = String(
      t['Direction'] ?? c.decision['direction'] ?? 'LONG',
    ).toUpperCase();
    const short = direction.includes('SHORT');
    const entryTime = this.parseTimestamp(t['EntryTime'] ?? c.timestamp).getTime();
    const all = candles
      .flatMap((x) => x.ticks)
      .filter((x) => x.ltp > 0 && this.time(x) >= entryTime)
      .sort((a, b) => this.time(a) - this.time(b));
    if (entry <= 0)
      return {
        status: 'NO_EXIT_DATA',
        direction,
        entryPrice: 0,
        gates: [],
        prematureExit: false,
        diagnosis: 'No captured entry price is available.',
      };
    const cfg: any = stock.configuration ?? {},
      ex: any = cfg['exit'] ?? cfg['Exit'] ?? {},
      policy: any = t['AdaptivePolicy'] ?? {};
    const cfgNumber = (...names: string[]): number =>
      this.num(this.configValue(cfg, ...names));
    const atr =
      this.num(t['ATR']) || this.num(c.indicators['ATR']) || this.num(stateTick?.indicators && this.indicatorLookup(stateTick.indicators, 'ATR')) || entry * 0.005;
    const stop = this.num(t['StopLoss']) || this.num(c.decision['stopLoss']) || this.num(stateTick?.stopLoss);
    const target =
      this.num(t['TargetPrice']) || this.num(c.decision['targetPrice']) || this.num(stateTick?.targetPrice);
    const trailingMultiplier =
      this.num(t['AdaptiveTrailingAtrMultiplier']) ||
      this.num(policy['TrailingAtrMultiplier']) ||
      this.num(
        ex['TrailingStopAtrMultiplier'] ?? ex['trailingStopAtrMultiplier'] ?? cfgNumber('trailingStopAtrMultiplier'),
      );
    const activation =
      this.num(t['TrailingActivationNetProfit']) ||
      this.num(
        ex['TrailingActivationNetProfit'] ?? ex['trailingActivationNetProfit'] ?? cfgNumber('trailingActivationNetProfit'),
      );
    const retention =
      this.num(t['PeakProfitRetentionPercent']) ||
      this.num(
        ex['TrailingProfitRetentionPercent'] ??
          ex['trailingProfitRetentionPercent'] ??
          cfgNumber('trailingProfitRetentionPercent'),
      );
    let highest = entry,
      lowest = entry,
      trailActivated = false,
      stopWould = false,
      targetWould = false,
      trailWould = false;
    let best: Tick | undefined, worst: Tick | undefined;
    for (const tick of all) {
      highest = Math.max(highest, tick.ltp);
      lowest = Math.min(lowest, tick.ltp);
      if (!best || (short ? tick.ltp < best.ltp : tick.ltp > best.ltp))
        best = tick;
      if (!worst || (short ? tick.ltp > worst.ltp : tick.ltp < worst.ltp))
        worst = tick;
      const gross =
        (short ? entry - tick.ltp : tick.ltp - entry) *
        Math.max(1, this.num(t['Quantity']));
      const charges = this.num(t['EstimatedCharges']);
      const net = gross - charges;
      if (!trailActivated && activation > 0 && net > activation) {
        trailActivated = true;
        trailWould = true;
      }
      if (stop > 0 && (short ? tick.ltp >= stop : tick.ltp <= stop))
        stopWould = true;
      if (target > 0 && (short ? tick.ltp <= target : tick.ltp >= target))
        targetWould = true;
      if (trailActivated && trailingMultiplier > 0) {
        const trail = short
          ? lowest + atr * trailingMultiplier
          : highest - atr * trailingMultiplier;
        if (short ? tick.ltp >= trail : tick.ltp <= trail) trailWould = true;
      }
    }
    const afterExit =
      exit > 0
        ? all.filter(
            (x) =>
              this.time(x) >= this.parseTimestamp(t['ExitTime'] ?? '').getTime(),
          )
        : [];
    const postExitBest = afterExit.length
      ? short
        ? Math.min(...afterExit.map((x) => x.ltp))
        : Math.max(...afterExit.map((x) => x.ltp))
      : undefined;
    const betterExit =
      best && exit > 0
        ? short
          ? best.ltp < exit * 0.997
          : best.ltp > exit * 1.003
        : false;
    const premature = !!(exit > 0 && betterExit);
    const gates: GateEvaluation[] = [];
    gates.push({
      name: 'Observed exit state',
      status: exit > 0 ? 'PASS' : 'UNKNOWN',
      actual: exit > 0 ? 1 : 0,
      required: 1,
      margin: exit > 0 ? 1 : -1,
      reason:
        exit > 0
          ? `Recorded exit at ₹${exit.toFixed(2)}.`
          : 'No recorded exit price.',
      source: exit > 0 ? 'captured' : 'unavailable',
    });
    gates.push({
      name: 'Protective stop path',
      status: stop > 0 ? (stopWould ? 'PASS' : 'FAIL') : 'UNKNOWN',
      actual: stop,
      required: stop || undefined,
      reason:
        stop > 0
          ? stopWould
            ? 'Future path touched the recorded stop.'
            : 'Future path never touched the recorded stop.'
          : 'Stop price unavailable.',
      source: stop > 0 ? 'replayed' : 'unavailable',
    });
    gates.push({
      name: 'Target path',
      status: target > 0 ? (targetWould ? 'PASS' : 'FAIL') : 'UNKNOWN',
      actual: target,
      required: target || undefined,
      reason:
        target > 0
          ? targetWould
            ? 'Future path reached the recorded target.'
            : 'Future path never reached the recorded target.'
          : 'Target price unavailable.',
      source: target > 0 ? 'replayed' : 'unavailable',
    });
    gates.push({
      name: 'Trailing activation',
      status: activation > 0 ? (trailWould ? 'PASS' : 'FAIL') : 'UNKNOWN',
      actual: activation,
      required: activation || undefined,
      reason:
        activation > 0
          ? trailWould
            ? 'Captured future path crossed the trailing activation net-profit threshold.'
            : 'Activation threshold was never crossed in the captured path.'
          : 'Trailing activation threshold unavailable.',
      source: activation > 0 ? 'replayed' : 'unavailable',
    });
    let diagnosis = 'No material exit mistake proven from the captured path.';
    if (premature)
      diagnosis = `Exit appears premature: the future path reached ₹${best!.ltp.toFixed(2)} after entry versus recorded exit ₹${exit.toFixed(2)}.`;
    if (stopWould && exit > 0 && !targetWould)
      diagnosis +=
        ' The protective stop was reachable before the target; review stop placement and adverse-move tolerance.';
    if (
      targetWould &&
      exit > 0 &&
      ((short && exit > target) || (!short && exit < target))
    )
      diagnosis +=
        ' The recorded exit occurred before the configured target was reached.';
    if (
      afterExit.length &&
      postExitBest !== undefined &&
      ((short && postExitBest < exit * 0.997) ||
        (!short && postExitBest > exit * 1.003))
    )
      diagnosis += ` Price continued favorably after the recorded exit, indicating potential profit-retention loss.`;
    return {
      status: exit > 0 ? 'EXITED' : 'OPEN',
      direction,
      entryPrice: entry,
      observedExitPrice: exit || undefined,
      observedExitReason: String(t['ExitReason'] ?? '') || undefined,
      gates,
      bestFutureExitPrice: best?.ltp,
      bestFutureExitTime: best ? best.exchangeTime || best.utc : undefined,
      worstFuturePrice: worst?.ltp,
      prematureExit: premature,
      stopWouldHaveTriggered: stopWould,
      targetWouldHaveTriggered: targetWould,
      trailingWouldHaveActivated: trailWould,
      diagnosis,
    };
  }

  private tickSpanSeconds(ticks: Tick[]): number {
    if (ticks.length < 2) return 0;
    const a = this.time(ticks[0]),
      b = this.time(ticks[ticks.length - 1]);
    return Math.max(0, (b - a) / 1000);
  }

  private evaluateGlobalSplit(
    params: any,
  ): GlobalConfigurationResult['validation'] {
    const stocks = this.stocks();
    let trainObs = 0,
      testObs = 0,
      trainBase = 0,
      trainOpt = 0,
      testBase = 0,
      testOpt = 0,
      baseLoss = 0,
      optLoss = 0;
    for (const stock of stocks) {
      const candles = stock.candles.filter((c) => c.candle.close > 0);
      const cut = Math.max(1, Math.floor(candles.length * 0.7));
      for (let i = 0; i < candles.length; i++) {
        const c = candles[i],
          d = c.decision,
          p = c.candle.close;
        const future = this.futureTicks(candles, i);
        if (!future.length || !p) continue;
        const gain = ((Math.max(p, ...future.map((t) => t.ltp)) - p) / p) * 100;
        const score = this.num(d['score']),
          conf = this.num(d['adaptiveConfidence']),
          rr = this.num(d['adaptiveRiskReward']),
          edge = this.num(d['adaptiveEdgeScore']),
          spread = this.num(
            this.indicatorLookup(c.indicators, 'SpreadPercent') ??
              c.ticks[0]?.spreadPct,
          );
        const current =
          String(d['signal'] ?? '').toUpperCase() === 'BUY' &&
          score >= this.params.minimumScore &&
          (!this.params.useAdaptiveScore ||
            conf >= this.params.minimumConfidence) &&
          (!this.params.useAdaptiveScore ||
            rr >= this.params.minimumRiskReward) &&
          (!this.params.useAdaptiveScore ||
            edge >= this.params.minimumEdgeScore) &&
          spread <= this.params.maximumSpreadPercent;
        const opt =
          String(d['signal'] ?? '').toUpperCase() === 'BUY' &&
          score >= params.minimumScore &&
          (!params.useAdaptiveScore || conf >= params.minimumConfidence) &&
          (!params.useAdaptiveScore || rr >= params.minimumRiskReward) &&
          (!params.useAdaptiveScore || edge >= params.minimumEdgeScore) &&
          spread <= params.maximumSpreadPercent;
        const isTrain = i < cut;
        if (isTrain) {
          trainObs++;
          trainBase += current
            ? Math.max(0, (p * (gain - this.params.minimumProfitPercent)) / 100)
            : 0;
          trainOpt += opt
            ? Math.max(0, (p * (gain - params.minimumProfitPercent)) / 100)
            : 0;
        } else {
          testObs++;
          testBase += current
            ? Math.max(0, (p * (gain - this.params.minimumProfitPercent)) / 100)
            : 0;
          testOpt += opt
            ? Math.max(0, (p * (gain - params.minimumProfitPercent)) / 100)
            : 0;
        }
        if (current && gain < 0) baseLoss++;
        if (opt && gain < 0) optLoss++;
      }
    }
    const trainImprovement = trainOpt - trainBase,
      testImprovement = testOpt - testBase,
      coverage =
        trainObs + testObs
          ? ((trainObs + testObs) /
              Math.max(
                1,
                stocks.reduce((n, s) => n + s.candles.length, 0),
              )) *
            100
          : 0;
    return {
      trainObservations: trainObs,
      testObservations: testObs,
      trainImprovement,
      testImprovement,
      coveragePercent: Math.min(100, coverage),
      maxLossIncrease: Math.max(0, optLoss - baseLoss),
      rollbackRule:
        'Do not promote if holdout improvement ≤ 0 or simulated losses increase; rollback to the prior configuration.',
    };
  }

  private indicatorKeys = [
    'EMA9',
    'EMA21',
    'EMA50',
    'EMA200',
    'VWAP',
    'AnchoredVWAP',
    'SuperTrend',
    'BollingerUpper',
    'BollingerMiddle',
    'BollingerLower',
    'RSI',
    'MACD',
    'MACDSignal',
    'MACDHistogram',
    'ADX',
    'RelativeVolume',
    'ATR',
    'Choppiness',
    'EMASlope9',
    'EMASlope21',
    'PullbackDistance',
    'DistanceFromEMA',
    'DistanceFromVWAP',
  ];
  private priceOverlayKeys = [
    'EMA9',
    'EMA21',
    'EMA50',
    'EMA200',
    'VWAP',
    'AnchoredVWAP',
    'SuperTrend',
    'BollingerUpper',
    'BollingerMiddle',
    'BollingerLower',
  ];
  private oscillatorKeys = [
    'RSI',
    'MACD',
    'MACDSignal',
    'MACDHistogram',
    'ADX',
  ];
  private indicatorLookup(
    source: Indicators | undefined,
    name: string,
  ): unknown {
    if (!source) return undefined;
    if (source[name] !== undefined) return source[name];
    const wanted = name.toLowerCase().replace(/[^a-z0-9]/g, '');
    const found = Object.keys(source).find(
      (k) => k.toLowerCase().replace(/[^a-z0-9]/g, '') === wanted,
    );
    return found ? source[found] : undefined;
  }
  private canonicalIndicatorName(name: string): string | undefined {
    const n = name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]/g, '');
    const a: Record<string, string> = {
      ema9: 'EMA9',
      ema21: 'EMA21',
      ema50: 'EMA50',
      ema200: 'EMA200',
      vwap: 'VWAP',
      anchoredvwap: 'AnchoredVWAP',
      avwap: 'AnchoredVWAP',
      supertrend: 'SuperTrend',
      bollinger: 'BollingerMiddle',
      bollingerupper: 'BollingerUpper',
      bollingerbandupper: 'BollingerUpper',
      bollingermiddle: 'BollingerMiddle',
      bollingerbandmiddle: 'BollingerMiddle',
      bollingerlower: 'BollingerLower',
      bollingerbandlower: 'BollingerLower',
      rsi: 'RSI',
      macd: 'MACD',
      macdsignal: 'MACDSignal',
      macdhistogram: 'MACDHistogram',
      macdhist: 'MACDHistogram',
      adx: 'ADX',
      relativevolume: 'RelativeVolume',
      relvolume: 'RelativeVolume',
      atr: 'ATR',
      volatility: 'Volatility',
      choppiness: 'Choppiness',
      emaslope9: 'EMASlope9',
      emaslope21: 'EMASlope21',
      pullbackdistance: 'PullbackDistance',
      distancefromema: 'DistanceFromEMA',
      distancefromvwap: 'DistanceFromVWAP',
      spreadpercent: 'SpreadPercent',
      spreadpct: 'SpreadPercent',
      signal: 'Signal',
      score: 'Score',
      finalscore: 'Score',
      adaptiveexpectednetvalue: 'AdaptiveExpectedNetValue',
      adaptiveedgescore: 'AdaptiveEdgeScore',
      adaptiveriskreward: 'AdaptiveRiskReward',
    };
    return a[n];
  }
  private tickIndicatorValue(index: number, key: string): unknown {
    const c = this.candles()[index],
      t = c?.ticks?.[c.ticks.length - 1];
    return (
      this.indicatorLookup(t?.indicators, key) ??
      this.indicatorLookup(c?.indicators, key)
    );
  }
  displayNumber(value: unknown, digits = 1, suffix = ''): string {
    if (value === undefined || value === null || String(value).trim() === '') return 'N/A';
    const n = Number(value);
    return Number.isFinite(n) ? `${n.toFixed(digits)}${suffix}` : 'N/A';
  }

  displayText(value: unknown, fallback = 'N/A'): string {
    if (value === undefined || value === null || String(value).trim() === '') return fallback;
    return String(value);
  }

  /** Template-safe accessors keep Angular strict template checking away from optional Tick fields. */
  tickScore(t: any): unknown {
    return t?.indicators?.['Score'];
  }

  candleSignal(c: any): unknown {
    const tick = c?.ticks?.[c?.ticks?.length - 1];
    return c?.decision?.['signal'] || tick?.decision;
  }

  candleScore(c: any): unknown {
    const tick = c?.ticks?.[c?.ticks?.length - 1];
    return c?.decision?.['score'] ?? tick?.indicators?.['Score'];
  }

  candleAdaptiveEdge(c: any): unknown {
    const tick = c?.ticks?.[c?.ticks?.length - 1];
    return c?.decision?.['adaptiveEdgeScore'] ?? tick?.adaptiveEdgeScore ?? tick?.indicators?.['AdaptiveEdgeScore'];
  }

  indicatorValue(name: string): string {
    const v = this.indicatorLookup(this.indicators(), name);
    return v === undefined || v === ''
      ? '—'
      : typeof v === 'number'
        ? v.toFixed(3)
        : String(v);
  }
  indicatorPresence(name: string): number {
    return this.candles().filter((c) => {
      const v =
        this.indicatorLookup(c.indicators, name) ??
        this.indicatorLookup(c.ticks?.[c.ticks.length - 1]?.indicators, name);
      return v !== undefined && v !== null && String(v) !== '';
    }).length;
  }
  indicatorLoadedCount(): number {
    return this.indicatorKeys.filter((k) => this.indicatorPresence(k) > 0)
      .length;
  }
  indicatorKeysForUi(): string[] {
    return this.indicatorKeys;
  }
  evaluationCoveragePercent(): number {
    const total = this.candles().length;
    return total ? (this.evaluatedObservationCount() / total) * 100 : 0;
  }
  indicatorAudit(): Array<{ name: string; available: number; usage: string }> {
    const direct = new Set(['SpreadPercent', 'ATR', 'Volatility']);
    const indirect = new Set([
      'EMA9',
      'EMA21',
      'EMA50',
      'EMA200',
      'VWAP',
      'AnchoredVWAP',
      'SuperTrend',
      'BollingerUpper',
      'BollingerMiddle',
      'BollingerLower',
      'RSI',
      'MACD',
      'MACDSignal',
      'MACDHistogram',
      'ADX',
      'RelativeVolume',
      'Choppiness',
      'EMASlope9',
      'EMASlope21',
      'PullbackDistance',
      'DistanceFromEMA',
      'DistanceFromVWAP',
    ]);
    return [
      ...new Set([...this.indicatorKeys, 'SpreadPercent', 'Volatility']),
    ].map((name) => ({
      name,
      available: this.indicatorPresence(name),
      usage: direct.has(name)
        ? name === 'SpreadPercent'
          ? 'Direct evaluation gate'
          : 'Direct regime classification / context'
        : indirect.has(name)
          ? 'Captured value; upstream Signal/Score may depend on it, but this page does not invent a separate threshold.'
          : 'Displayed / contextual only',
    }));
  }
  chartOverlayKeys(): string[] {
    return this.priceOverlayKeys.filter(
      (k) =>
        this.toggles[k.startsWith('Bollinger') ? 'Bollinger' : k] &&
        this.candles().some((_, i) => {
          const v = this.tickIndicatorValue(i, k);
          return (
            v !== undefined &&
            v !== null &&
            String(v) !== '' &&
            Number.isFinite(this.num(v))
          );
        }),
    );
  }
  chartOscillatorKeys(): string[] {
    return this.oscillatorKeys.filter(
      (k) =>
        this.toggles[k] &&
        this.candles().some((_, i) => {
          const v = this.tickIndicatorValue(i, k);
          return (
            v !== undefined &&
            v !== null &&
            String(v) !== '' &&
            Number.isFinite(this.num(v))
          );
        }),
    );
  }
  chartSeriesPoints(key: string): string {
    return this.chartIndicatorPoints(
      key,
      this.chartOscillatorKeys().includes(key) ? 1100 : 1100,
      this.chartOscillatorKeys().includes(key) ? 160 : 420,
    );
  }
  chartIndicatorPoints(key: string, width = 1100, height = 420): string {
    const cs = this.candles();
    if (!cs.length) return '';
    const vals = cs
      .map((_, i) => this.num(this.tickIndicatorValue(i, key)))
      .filter((v) => Number.isFinite(v));
    if (!vals.length) return '';
    const min = Math.min(...vals),
      max = Math.max(...vals),
      range = Math.max(0.0001, max - min),
      pad = 22;
    const x = (i: number) =>
      pad + i * ((width - pad * 2) / Math.max(1, cs.length - 1));
    const y = (v: number) =>
      height - pad - ((v - min) / range) * (height - pad * 2);
    return cs
      .map((_, i) => {
        const raw = this.tickIndicatorValue(i, key);
        if (raw === undefined || raw === null || String(raw) === '') return '';
        const v = this.num(raw);
        return `${x(i).toFixed(1)},${y(v).toFixed(1)}`;
      })
      .filter(Boolean)
      .join(' ');
  }
  chartOscillatorY(key: string, tick: Tick): number {
    const vals = (this.stock()?.candles.flatMap((c) => c.ticks) ?? [])
      .map((t) => this.num(this.indicatorLookup(t.indicators, key)))
      .filter((v) => Number.isFinite(v));
    if (!vals.length) return 80;
    const min = Math.min(...vals),
      max = Math.max(...vals),
      range = Math.max(0.0001, max - min),
      v = this.num(this.indicatorLookup(tick.indicators, key));
    return 140 - 18 - ((v - min) / range) * 104;
  }
  private time(t: Tick): number {
    return this.parseTimestamp(t.utc || t.exchangeTime).getTime();
  }
  num(v: unknown): number {
    const n = Number(v);
    return Number.isFinite(n) ? n : 0;
  }
  /** Format workbook timestamps as Indian Standard Time everywhere in the UI.
   *  Plain ISO/date-time strings from the workbook are treated as UTC because
   *  the lifecycle source exposes TimestampUtc; explicit offsets are respected.
   */
  istTime(value: unknown): string {
    const d = this.parseTimestamp(value);
    if (Number.isNaN(d.getTime())) return String(value ?? '');
    return d.toLocaleTimeString('en-IN', {
      timeZone: 'Asia/Kolkata',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  }

  private format(v: string): string {
    return this.istTime(v);
  }

  private parseTimestamp(value: unknown): Date {
    if (value instanceof Date) return value;
    const raw = String(value ?? '').trim();
    if (!raw) return new Date(NaN);
    // Explicit timezone/offset: let Date convert it correctly.
    if (/Z$/i.test(raw) || /[+-]\d{2}:?\d{2}$/.test(raw)) return new Date(raw);
    // Workbook UTC fields are sometimes exported without a trailing Z.
    // Treat ISO/date-time strings as UTC instead of browser-local time.
    if (/^\d{4}-\d{2}-\d{2}[T ]/.test(raw)) return new Date(raw.replace(' ', 'T') + 'Z');
    // Excel/local time-only values: anchor them to today's UTC date.
    if (/^\d{1,2}:\d{2}(:\d{2}(?:\.\d+)?)?$/.test(raw)) {
      const today = new Date();
      const date = today.toISOString().slice(0, 10);
      return new Date(`${date}T${raw}Z`);
    }
    return new Date(raw);
  }

  private futureTicks(c: CandleCapture[], i: number): Tick[] {
    return c
      .slice(i + 1)
      .flatMap((x) => x.ticks)
      .filter((t) => t.ltp > 0)
      .sort((a, b) => this.time(a) - this.time(b));
  }

  private evaluationReady(c: CandleCapture): boolean {
    const d = c.decision ?? {};
    const signal = String(d['signal'] ?? '').trim();
    const score = d['score'];
    const spread =
      this.indicatorLookup(c.indicators, 'SpreadPercent') ??
      c.ticks[0]?.spreadPct;
    if (
      !signal ||
      score === undefined ||
      score === null ||
      String(score).trim() === ''
    )
      return false;
    if (spread === undefined || spread === null || String(spread).trim() === '')
      return false;
    if (!this.indicatorSnapshotReady(c)) return false;
    if (this.params.useAdaptiveScore) {
      if (
        d['adaptiveConfidence'] === undefined ||
        d['adaptiveRiskReward'] === undefined ||
        d['adaptiveEdgeScore'] === undefined
      )
        return false;
    }
    return true;
  }
  private gates(
    d: Record<string, string | number | boolean>,
    score: number,
    conf: number,
    rr: number,
    edge: number,
    spread: number,
    technical: boolean,
    sp: boolean,
    cp: boolean,
    rp: boolean,
    ep: boolean,
    spreadPass: boolean,
  ): string[] {
    const g: string[] = [];
    if (!technical) g.push(`Technical signal=${d['signal'] ?? 'HOLD'}`);
    if (!sp)
      g.push(
        `Minimum score ${this.params.minimumScore} blocked score ${score}`,
      );
    if (!cp)
      g.push(
        `Minimum confidence ${this.params.minimumConfidence} blocked confidence ${conf.toFixed(1)}`,
      );
    if (!rp)
      g.push(
        `Minimum R/R ${this.params.minimumRiskReward.toFixed(2)} blocked R/R ${rr.toFixed(2)}`,
      );
    if (!ep)
      g.push(
        `Minimum edge ${this.params.minimumEdgeScore} blocked edge ${edge.toFixed(1)}`,
      );
    if (!spreadPass)
      g.push(
        `Maximum spread ${this.params.maximumSpreadPercent}% blocked spread ${spread.toFixed(2)}%`,
      );
    const gf = String(d['gateFailures'] ?? '');
    if (gf && g.length === 0) g.push(gf.split('|')[0]);
    return g;
  }
  private gateValue(
    g: string,
    s: number,
    c: number,
    rr: number,
    e: number,
    sp: number,
  ): number {
    if (/score/i.test(g)) return s;
    if (/confidence/i.test(g)) return c;
    if (/R\/R/i.test(g)) return rr;
    if (/edge/i.test(g)) return e;
    if (/spread/i.test(g)) return sp;
    return 0;
  }
  private gateSuggestion(
    g: string,
    s: number,
    c: number,
    rr: number,
    e: number,
    sp: number,
  ): number {
    if (/score/i.test(g)) return Math.max(0, s);
    if (/confidence/i.test(g)) return Math.max(0, c);
    if (/R\/R/i.test(g)) return Math.max(0, rr);
    if (/edge/i.test(g)) return Math.max(0, e);
    if (/spread/i.test(g))
      return Math.max(sp, this.params.maximumSpreadPercent);
    return 0;
  }
  private recordRecommendation(
    m: Map<string, { count: number; gain: number; values: number[] }>,
    gate: string,
    gain: number,
  ) {
    const key = /score/i.test(gate)
      ? 'minimumScore'
      : /confidence/i.test(gate)
        ? 'minimumConfidence'
        : /R\/R/i.test(gate)
          ? 'minimumRiskReward'
          : /edge/i.test(gate)
            ? 'minimumEdgeScore'
            : /spread/i.test(gate)
              ? 'maximumSpreadPercent'
              : '';
    if (!key) return;
    const x = m.get(key) ?? { count: 0, gain: 0, values: [] };
    x.count++;
    x.gain += gain;
    x.values.push(
      this.gateValue(
        gate,
        this.params.minimumScore,
        this.params.minimumConfidence,
        this.params.minimumRiskReward,
        this.params.minimumEdgeScore,
        this.params.maximumSpreadPercent,
      ),
    );
    m.set(key, x);
  }
  private buildRecommendations(
    m: Map<string, { count: number; gain: number; values: number[] }>,
  ): ConfigurationRecommendation[] {
    return [...m.entries()]
      .map(([parameter, x]) => {
        const current = (this.params as any)[parameter] as number;
        const suggested =
          parameter === 'minimumScore'
            ? Math.max(
                0,
                Math.min(current - 5, Math.floor(Math.min(...x.values))),
              )
            : parameter === 'minimumConfidence'
              ? Math.max(
                  0,
                  Math.min(current - 5, Math.floor(Math.min(...x.values))),
                )
              : parameter === 'minimumRiskReward'
                ? Math.max(0, Math.min(current - 0.25, Math.min(...x.values)))
                : parameter === 'minimumEdgeScore'
                  ? Math.max(
                      0,
                      Math.min(current - 5, Math.floor(Math.min(...x.values))),
                    )
                  : Math.max(current, Math.max(...x.values));
        return {
          parameter,
          current,
          suggested,
          reason: `${x.count} profitable opportunities were blocked by this gate; their average subsequent move was ${(x.gain / x.count).toFixed(2)}%.`,
          evidenceCount: x.count,
          expectedImprovement: x.gain,
        };
      })
      .sort((a, b) => b.expectedImprovement - a.expectedImprovement)
      .slice(0, 6);
  }
  private recommendation(
    reasons: string[],
    recs: ConfigurationRecommendation[],
    missed: number,
    actualLosses: number,
    avoidable: number,
  ): string {
    if (!missed && !actualLosses)
      return 'No material missed-entry or losing-trade pattern was found in this captured window.';
    if (recs.length)
      return `The strongest evidence points to ${recs[0].parameter}: test ${recs[0].current} → ${recs[0].suggested}. This is an evidence-based counterfactual from the captured window, not a guarantee of future profitability.`;
    if (avoidable)
      return `${avoidable} losing trade(s) appear potentially avoidable through exit timing or confirmation changes.`;
    return `The dominant issue is ${reasons[0] ?? 'entry/exit gating'}. Validate the captured pattern over multiple sessions before changing live settings.`;
  }
  private bestPair(
    ticks: Tick[],
  ): {
    entry: number;
    exit: number;
    entryTime: string;
    exitTime: string;
  } | null {
    let min = Infinity,
      entry: Tick | undefined,
      best = -Infinity,
      out: null | {
        entry: number;
        exit: number;
        entryTime: string;
        exitTime: string;
      } = null;
    for (const t of ticks) {
      if (t.ltp < min) {
        min = t.ltp;
        entry = t;
      }
      if (entry && t.ltp - entry.ltp > best) {
        best = t.ltp - entry.ltp;
        out = {
          entry: entry.ltp,
          exit: t.ltp,
          entryTime: entry.exchangeTime || entry.utc,
          exitTime: t.exchangeTime || t.utc,
        };
      }
    }
    return out;
  }
  private firstPeak(ticks: Tick[], entry: number): Tick | undefined {
    let best = entry,
      bestTick: Tick | undefined;
    for (const t of ticks) {
      if (t.ltp > best) {
        best = t.ltp;
        bestTick = t;
      }
    }
    return bestTick;
  }
  private isAvoidableLoss(c: CandleCapture, future: Tick[]): boolean {
    const entry = this.num(c.actualTrade?.['EntryPrice']);
    if (!entry) return false;
    const favorable = future.some((t) => t.ltp >= entry * 1.002);
    const adverse = future.some((t) => t.ltp <= entry * 0.998);
    return favorable && adverse;
  }
  private bestExitAfterEntry(c: CandleCapture, ticks: Tick[]): number {
    const entry = this.num(c.actualTrade?.['EntryPrice']);
    const after = ticks.filter(
      (t) =>
        this.time(t) >=
          new Date(
            String(c.actualTrade?.['EntryTime'] ?? c.timestamp),
          ).getTime() && t.ltp > entry,
    );
    return after.length ? Math.max(...after.map((t) => t.ltp)) : 0;
  }

  private objective(r: {
    netProfit: number;
    missed: number;
    missedProfit: number;
    losses: number;
  }): number {
    return r.netProfit + r.missedProfit * 0.75 - r.losses * 10;
  }

  private evaluateGlobal(params: {
    minimumScore: number;
    minimumConfidence: number;
    minimumRiskReward: number;
    minimumEdgeScore: number;
    maximumSpreadPercent: number;
    minimumProfitPercent: number;
    useAdaptiveScore: boolean;
  }): {
    netProfit: number;
    missed: number;
    missedProfit: number;
    losses: number;
    observations: number;
    profitableOpportunities: number;
  } {
    let netProfit = 0,
      missed = 0,
      missedProfit = 0,
      losses = 0,
      observations = 0,
      profitableOpportunities = 0;
    for (const stock of this.stocks()) {
      const candles = stock.candles.filter((c) => c.candle.close > 0);
      for (let i = 0; i < candles.length; i++) {
        const c = candles[i],
          d = c.decision,
          p = c.candle.close;
        if (!p || !this.evaluationReady(c)) continue;
        observations++;
        const score = this.num(d['score']),
          conf = this.num(d['adaptiveConfidence']),
          rr = this.num(d['adaptiveRiskReward']),
          edge = this.num(d['adaptiveEdgeScore']);
        const spread = this.num(
          this.indicatorLookup(c.indicators, 'SpreadPercent') ??
            c.ticks[0]?.spreadPct,
        );
        const technical = String(d['signal'] ?? '').toUpperCase() === 'BUY';
        const passes =
          technical &&
          score >= params.minimumScore &&
          (!params.useAdaptiveScore || conf >= params.minimumConfidence) &&
          (!params.useAdaptiveScore || rr >= params.minimumRiskReward) &&
          (!params.useAdaptiveScore || edge >= params.minimumEdgeScore) &&
          spread <= params.maximumSpreadPercent;
        const future = this.futureTicks(candles, i);
        if (!future.length) continue;
        const best = Math.max(p, ...future.map((t) => t.ltp)),
          gain = p ? ((best - p) / p) * 100 : 0;
        if (gain > params.minimumProfitPercent) profitableOpportunities++;
        if (passes && gain > params.minimumProfitPercent) {
          netProfit += (p * (gain - params.minimumProfitPercent)) / 100;
        } else if (!passes && gain > params.minimumProfitPercent) {
          missed++;
          missedProfit += (p * (gain - params.minimumProfitPercent)) / 100;
        }
        if (passes) {
          const worst = Math.min(p, ...future.map((t) => t.ltp));
          if (worst < p * (1 - 0.003)) losses++;
        }
      }
    }
    return {
      netProfit,
      missed,
      missedProfit,
      losses,
      observations,
      profitableOpportunities,
    };
  }

  private analyzeAllTradePaths(
    stock: StockCapture,
    ticks: Tick[],
  ): FuturePathAnalysis[] {
    const paths: FuturePathAnalysis[] = [];
    const seen = new Set<string>();
    for (const c of stock.candles) {
      const entry = this.num(c.actualTrade?.['EntryPrice']);
      if (entry <= 0) continue;
      const key = String(c.actualTrade?.['EntryTime'] ?? c.timestamp);
      if (seen.has(key)) continue;
      seen.add(key);
      const path = this.analyzeActualTradePathForTrade(
        c.actualTrade,
        c.timestamp,
        ticks,
      );
      if (path) paths.push(path);
    }
    return paths;
  }

  private analyzeActualTradePathForTrade(
    trade: ActualTrade,
    candleTimestamp: string,
    ticks: Tick[],
  ): FuturePathAnalysis | undefined {
    const entryPrice = this.num(trade?.['EntryPrice']);
    if (!entryPrice) return undefined;
    const entryTimeRaw = String(trade?.['EntryTime'] ?? candleTimestamp),
      entryTime = new Date(entryTimeRaw).getTime();
    const exitPrice = this.num(trade?.['ExitPrice']);
    const exitTimeRaw = String(trade?.['ExitTime'] ?? '');
    const exitTime = exitTimeRaw ? new Date(exitTimeRaw).getTime() : NaN;
    const fromEntry = ticks.filter(
      (t) => this.time(t) >= entryTime && t.ltp > 0,
    );
    if (!fromEntry.length) return undefined;
    const maxTick = fromEntry.reduce(
        (a, b) => (b.ltp > a.ltp ? b : a),
        fromEntry[0],
      ),
      minTick = fromEntry.reduce(
        (a, b) => (b.ltp < a.ltp ? b : a),
        fromEntry[0],
      );
    const afterExit = Number.isFinite(exitTime)
      ? fromEntry.filter((t) => this.time(t) >= exitTime)
      : [];
    const realized = exitPrice
      ? ((exitPrice - entryPrice) / entryPrice) * 100
      : 0;
    const favorable = ((maxTick.ltp - entryPrice) / entryPrice) * 100;
    const adverse = ((minTick.ltp - entryPrice) / entryPrice) * 100;
    const left = Math.max(0, favorable - realized);
    let diagnosis = 'No material future-path mistake detected.';
    if (realized < 0 && favorable > Math.abs(realized) + 0.3)
      diagnosis = `Entry was followed by a favorable move of ${favorable.toFixed(2)}%, but the realized result was ${realized.toFixed(2)}%. Exit timing or exit gates likely surrendered profit.`;
    else if (left > 0.5)
      diagnosis = `The trade exited before the captured best price and left approximately ${left.toFixed(2)} percentage points on the table.`;
    else if (adverse < -0.5)
      diagnosis = `The path contained ${Math.abs(adverse).toFixed(2)}% adverse movement after entry; stop/confirmation behavior should be reviewed.`;
    if (afterExit.length) {
      const postExitMax = Math.max(...afterExit.map((t) => t.ltp));
      if (postExitMax > exitPrice * 1.003)
        diagnosis += ` Price later recovered above the recorded exit by ${(((postExitMax - exitPrice) / exitPrice) * 100).toFixed(2)}%.`;
    }
    return {
      entryTime: entryTimeRaw,
      exitTime: exitTimeRaw || undefined,
      entryPrice,
      actualExitPrice: exitPrice || undefined,
      maxPrice: maxTick.ltp,
      minPrice: minTick.ltp,
      bestExitPrice: maxTick.ltp,
      bestExitTime: maxTick.exchangeTime || maxTick.utc,
      maxFavorablePercent: favorable,
      maxAdversePercent: adverse,
      realizedPercent: realized,
      leftOnTablePercent: left,
      diagnosis,
    };
  }

  private analyzeActualTradePath(
    stock: StockCapture,
    ticks: Tick[],
  ): FuturePathAnalysis | undefined {
    return this.analyzeAllTradePaths(stock, ticks)[0];
  }

  configurationSourceLabel(): string {
    return this.stock()?.configurationSource ?? '';
  }
  formatConfigurationValue(value: unknown): string {
    if (value === null || value === undefined || value === '') return '—';
    if (typeof value === 'object') {
      try {
        return JSON.stringify(value);
      } catch {
        return String(value);
      }
    }
    return String(value);
  }

  private indicatorSnapshotReady(c: CandleCapture): boolean {
    const i = c.indicators ?? {};
    const required = ['EMA9', 'EMA21', 'EMA50', 'VWAP', 'RSI', 'ATR'];
    const present = required.filter((k) => {
      const v = this.indicatorLookup(i, k);
      return v !== undefined && v !== null && String(v).trim() !== '';
    });
    // The exporter serializes an uninitialized indicator snapshot as zero-valued
    // properties. Treat an all-zero core snapshot as not loaded; zero is not a
    // usable EMA/VWAP/ATR price-derived snapshot for a real stock.
    if (present.length < required.length) return false;
    const priceDerived = ['EMA9', 'EMA21', 'EMA50', 'VWAP', 'ATR'].map((k) =>
      this.num(this.indicatorLookup(i, k)),
    );
    if (priceDerived.every((v) => v === 0)) return false;
    const rsi = this.num(this.indicatorLookup(i, 'RSI'));
    if (
      rsi === 0 &&
      this.num(this.indicatorLookup(i, 'Score')) === 0 &&
      String(this.indicatorLookup(i, 'Signal') ?? '').trim() === ''
    )
      return false;
    return true;
  }

  private configValue(
    config: Record<string, unknown>,
    ...names: string[]
  ): unknown {
    const wanted = names.map((n) => n.toLowerCase().replace(/[^a-z0-9]/g, ''));
    const entries = Object.entries(config);
    for (const [key, value] of entries) {
      const normalized = key.toLowerCase().replace(/[^a-z0-9]/g, '');
      if (
        wanted.includes(normalized) ||
        wanted.some((w) => normalized.endsWith(w))
      )
        return value;
    }
    return undefined;
  }

  private loadParametersFromConfiguration(
    config: Record<string, unknown>,
  ): void {
    const tc: any = config;
    const e: any = tc['equity'] ?? tc['Equity'] ?? {};
    const d: any = tc['dynamicEvaluation'] ?? tc['DynamicEvaluation'] ?? {};
    const v: any = tc['validation'] ?? tc['Validation'] ?? {};
    this.params.minimumScore = this.num(
      e['minimumFinalScore'] ??
        e['MinimumFinalScore'] ??
        this.configValue(config, 'minimumFinalScore') ??
        this.params.minimumScore,
    );
    this.params.minimumConfidence = this.num(
      e['minimumConfidence'] ??
        e['MinimumConfidence'] ??
        d['minimumEntryScore'] ??
        d['MinimumEntryScore'] ??
        this.configValue(config, 'minimumConfidence', 'minimumEntryScore') ??
        this.params.minimumConfidence,
    );
    this.params.minimumRiskReward = this.num(
      d['minimumRiskReward'] ??
        d['MinimumRiskReward'] ??
        e['minimumRiskReward'] ??
        e['MinimumRiskReward'] ??
        this.configValue(config, 'minimumRiskReward') ??
        this.params.minimumRiskReward,
    );
    this.params.minimumExpectedNetValue = this.num(
      d['minimumExpectedNetValue'] ??
        d['MinimumExpectedNetValue'] ??
        this.configValue(config, 'minimumExpectedNetValue') ??
        this.params.minimumExpectedNetValue,
    );
    this.params.minimumEdgeScore = this.num(
      d['minimumEdgeScore'] ??
        d['MinimumEdgeScore'] ??
        this.configValue(config, 'minimumEdgeScore') ??
        this.params.minimumEdgeScore,
    );
    this.params.minimumProfitPercent = this.num(
      e['minimumRoiPercent'] ??
        e['MinimumRoiPercent'] ??
        v['minimumGainPercent'] ??
        v['MinimumGainPercent'] ??
        this.configValue(config, 'minimumRoiPercent', 'minimumGainPercent') ??
        this.params.minimumProfitPercent,
    );
    this.params.maximumSpreadPercent = this.num(
      e['maximumSpreadPercent'] ??
        e['MaximumSpreadPercent'] ??
        this.configValue(config, 'maximumSpreadPercent') ??
        this.params.maximumSpreadPercent,
    );
  }

  private async readWorkbook(file: File): Promise<StockCapture> {
    const buffer = await file.arrayBuffer();
    const wb = XLSX.read(buffer, { type: 'array', cellDates: true });
    if (!wb.SheetNames.length) throw new Error('Workbook contains no sheets.');

    // The current backend export contains a lifecycle tick sheet plus a
    // "Current Configuration" sheet. Never treat the configuration sheet as
    // market data and never fall back to hard-coded simulation parameters when
    // the snapshot is present.
    let lifecycleSheetName = wb.SheetNames.find((name) => {
      const rows = XLSX.utils.sheet_to_json<unknown[]>(wb.Sheets[name], {
        header: 1,
        raw: true,
        defval: '',
      });
      return rows.some(
        (r) =>
          String((r as unknown[])[0] ?? '')
            .trim()
            .toLowerCase() === 'ticknumber',
      );
    });
    if (!lifecycleSheetName)
      throw new Error(
        'No lifecycle tick table found. Expected a TickNumber header.',
      );

    const configuration: Record<string, unknown> = {};
    let configurationSource = '';
    let configurationCapturedAt = '';
    const configName = wb.SheetNames.find((name) =>
      /current\s*configuration|configuration/i.test(name),
    );
    if (configName) {
      const rows = XLSX.utils.sheet_to_json<unknown[]>(wb.Sheets[configName], {
        header: 1,
        raw: true,
        defval: '',
      });
      configurationSource = configName;
      for (const row of rows) {
        const r = row as unknown[];
        if (!r.length) continue;
        const key = String(r[0] ?? '').trim();
        if (!key || /^setting$|^path$|^configuration$/i.test(key)) continue;
        const value = this.value(r.length > 1 ? r[1] : '');
        configuration[key] = value;
        if (/capture(d)?\s*(at|timestamp)|timestamp/i.test(key))
          configurationCapturedAt = String(value ?? '');
      }
    }

    const sheet = wb.Sheets[lifecycleSheetName];
    const rows = XLSX.utils.sheet_to_json<unknown[]>(sheet, {
      header: 1,
      raw: true,
      defval: '',
    });
    const headerIndex = rows.findIndex(
      (r: unknown[]) => String(r[0] ?? '').trim() === 'TickNumber',
    );
    const headers = (rows[headerIndex] as unknown[]).map((x) =>
      String(x ?? '').trim(),
    );
    const indexOf = (name: string) => {
      const w = name.toLowerCase().replace(/[^a-z0-9]/g, '');
      return headers.findIndex(
        (h) => h.toLowerCase().replace(/[^a-z0-9]/g, '') === w,
      );
    };
    const firstIndex = (...names: string[]) => {
      for (const name of names) {
        const idx = indexOf(name);
        if (idx >= 0) return idx;
      }
      return -1;
    };
    const ticks: Tick[] = [];
    const candles: CandleCapture[] = [];
    const metadata: Record<string, string | number | boolean> = {};
    for (let i = 0; i < headerIndex; i++) {
      const r = rows[i] as unknown[];
      if (String(r?.[0] ?? '').trim())
        metadata[String(r[0]).trim()] = this.value(r[1]);
    }
    const actual: ActualTrade = {};
    for (const r0 of rows.slice(headerIndex + 1)) {
      const r = r0 as unknown[];
      if (!r.length || String(r[indexOf('TickNumber')] ?? '').trim() === '')
        continue;
      const indicators: Indicators = {};
      for (let j = 0; j < headers.length; j++) {
        const header = headers[j].trim();
        if (!header) continue;
        const rawName = header.toLowerCase().startsWith('indicator.')
          ? header.substring(header.indexOf('.') + 1)
          : header;
        const canonical = this.canonicalIndicatorName(rawName);
        if (canonical) indicators[canonical] = this.value(r[j]);
      }
      const loadedDecisionFields: string[] = [];
      const fieldAliases: Record<string, string[]> = {
        Signal: ['Signal', 'Indicator.Signal', 'Decision'],
        Score: [
          'Score',
          'Indicator.Score',
          'FinalScore',
          'Indicator.FinalScore',
        ],
        Confidence: ['Confidence', 'AdaptiveConfidence'],
        AdaptiveRiskReward: [
          'AdaptiveRiskReward',
          'Indicator.AdaptiveRiskReward',
          'RiskReward',
        ],
        AdaptiveEdgeScore: [
          'AdaptiveEdgeScore',
          'Indicator.AdaptiveEdgeScore',
          'EdgeScore',
        ],
        AdaptiveExpectedNetValue: [
          'AdaptiveExpectedNetValue',
          'Indicator.AdaptiveExpectedNetValue',
          'ExpectedNetValue',
        ],
        SpreadPercent: ['SpreadPercent', 'SpreadPct'],
      };
      for (const [field, aliases] of Object.entries(fieldAliases)) {
        const idx = firstIndex(...aliases);
        if (idx >= 0 && String(r[idx] ?? '').trim() !== '')
          loadedDecisionFields.push(field);
      }
      const tick: Tick = {
        n: this.num(r[indexOf('TickNumber')]),
        utc: this.iso(r[indexOf('TimestampUtc')]),
        exchangeTime: this.iso(r[indexOf('ExchangeTime')]),
        sequence: this.num(r[indexOf('SequenceNumber')]),
        ltp: this.num(r[indexOf('Price')]),
        bid: this.num(r[indexOf('Bid')]),
        ask: this.num(r[indexOf('Ask')]),
        spread: this.num(r[indexOf('Spread')]),
        spreadPct: this.num(r[firstIndex('SpreadPercent', 'SpreadPct')]),
        open: this.num(r[indexOf('Price')]),
        high: this.num(r[indexOf('High')]),
        low: this.num(r[indexOf('Low')]),
        close: this.num(r[indexOf('Price')]),
        ltq: 0,
        avgPrice: 0,
        dayVolume: this.num(r[indexOf('Volume')]),
        buyQty: 0,
        sellQty: 0,
        stage: String(r[indexOf('Stage')] ?? ''),
        decision: String(r[indexOf('Decision')] ?? ''),
        decisionReason: String(r[indexOf('DecisionReason')] ?? ''),
        status: String(r[indexOf('Status')] ?? ''),
        movementScore: this.num(r[indexOf('MovementScore')]),
        trendStrength: this.num(r[indexOf('TrendStrength')]),
        trendStability: this.num(r[indexOf('TrendStability')]),
        recoveryScore: this.num(r[indexOf('RecoveryScore')]),
        breakoutStrength: this.num(r[indexOf('BreakoutStrength')]),
        noiseScore: this.num(r[indexOf('NoiseScore')]),
        tickQualityScore: this.num(r[indexOf('TickQualityScore')]),
        confidence: this.num(r[firstIndex('Confidence', 'AdaptiveConfidence')]),
        adaptiveExpectedNetValue: this.num(
          r[firstIndex('AdaptiveExpectedNetValue', 'ExpectedNetValue')],
        ),
        adaptiveEdgeScore: this.num(
          r[firstIndex('AdaptiveEdgeScore', 'EdgeScore')],
        ),
        adaptiveRiskReward: this.num(
          r[firstIndex('AdaptiveRiskReward', 'RiskReward')],
        ),
        entryPrice: this.num(r[indexOf('EntryPrice')]),
        exitPrice: this.num(r[indexOf('ExitPrice')]),
        stopLoss: this.num(r[indexOf('StopLoss')]),
        targetPrice: this.num(r[indexOf('TargetPrice')]),
        exitReason: String(r[indexOf('ExitReason')] ?? ''),
        indicators,
      };
      ticks.push(tick);
      const close = tick.ltp;
      const candle: CandleCapture = {
        index: candles.length,
        timestamp: tick.utc || tick.exchangeTime,
        candle: {
          timestamp: tick.utc || tick.exchangeTime,
          open: close,
          high: close,
          low: close,
          close,
          volume: tick.dayVolume,
        },
        indicators,
        decision: (() => {
          const d: Record<string, string | number | boolean> = {};
          const signal =
            indicators['Signal'] !== undefined
              ? String(indicators['Signal'])
              : tick.decision;
          if (signal !== undefined && String(signal).trim() !== '')
            d['signal'] = String(signal);
          if (
            indicators['Score'] !== undefined &&
            String(indicators['Score']).trim() !== ''
          )
            d['score'] = this.num(indicators['Score']);
          if (tick.decisionReason) d['reason'] = tick.decisionReason;
          if (tick.status) d['status'] = tick.status;
          if (tick.stage) d['stage'] = tick.stage;
          if (tick.entryPrice && tick.entryPrice > 0)
            d['entryPrice'] = tick.entryPrice;
          if (tick.exitPrice && tick.exitPrice > 0)
            d['exitPrice'] = tick.exitPrice;
          if (tick.stopLoss && tick.stopLoss > 0) d['stopLoss'] = tick.stopLoss;
          if (tick.targetPrice && tick.targetPrice > 0)
            d['targetPrice'] = tick.targetPrice;
          if (loadedDecisionFields.includes('Confidence'))
            d['adaptiveConfidence'] = this.num(tick.confidence);
          if (loadedDecisionFields.includes('AdaptiveRiskReward'))
            d['adaptiveRiskReward'] = this.num(tick.adaptiveRiskReward);
          if (loadedDecisionFields.includes('AdaptiveEdgeScore'))
            d['adaptiveEdgeScore'] = this.num(tick.adaptiveEdgeScore);
          if (loadedDecisionFields.includes('AdaptiveExpectedNetValue'))
            d['adaptiveExpectedNetValue'] = this.num(
              tick.adaptiveExpectedNetValue,
            );
          return d;
        })(),
        virtualTrade: {
          movementScore: tick.movementScore ?? 0,
          trendStrength: tick.trendStrength ?? 0,
          trendStability: tick.trendStability ?? 0,
          recoveryScore: tick.recoveryScore ?? 0,
          breakoutStrength: tick.breakoutStrength ?? 0,
          noiseScore: tick.noiseScore ?? 0,
          tickQualityScore: tick.tickQualityScore ?? 0,
          priceSlope: 0,
          stage: tick.stage ?? '',
          tickCount: Math.max(0, tick.n),
          maximumObservationTicks: Math.max(0, tick.n),
        },
        actualTrade: {},
        ticks: [tick],
        loadedDecisionFields,
      };
      candles.push(candle);
    }
    if (!ticks.length)
      throw new Error('Lifecycle workbook contains no tick rows.');

    const entryPrice = this.num(metadata['Entry Price']);
    const exitPrice = this.num(metadata['Exit Price']);
    const entryTime = String(metadata['Entry Time UTC'] ?? '');
    const exitTime = String(metadata['Exit Time UTC'] ?? '');
    const actualBase: ActualTrade = {
      Status: metadata['Status'] ?? ticks[ticks.length - 1].status ?? '',
      EntryPrice: entryPrice,
      ExitPrice: exitPrice,
      EntryTime: entryTime,
      ExitTime: exitTime,
      ExitReason:
        metadata['Exit Reason'] ??
        ticks.find((x) => x.exitReason)?.exitReason ??
        '',
      Quantity: this.num(metadata['Quantity']),
      StopLoss:
        this.num(metadata['Stop Loss']) ||
        ticks.find((x) => (x.stopLoss ?? 0) > 0)?.stopLoss ||
        0,
      TargetPrice:
        this.num(metadata['Target Price']) ||
        ticks.find((x) => (x.targetPrice ?? 0) > 0)?.targetPrice ||
        0,
      EstimatedCharges: this.num(metadata['Estimated Charges']),
      EstimatedNetProfit: this.num(metadata['Estimated Net Profit']),
      Confidence: this.num(metadata['Entry Confidence']),
      Strategy: metadata['Strategy'] ?? '',
    };
    const nearest = (target: string, fallbackPrice: number): number => {
      const t = target ? new Date(target).getTime() : NaN;
      if (Number.isFinite(t)) {
        let best = 0,
          delta = Infinity;
        candles.forEach((x, i) => {
          const d = Math.abs(new Date(x.timestamp).getTime() - t);
          if (Number.isFinite(d) && d < delta) {
            delta = d;
            best = i;
          }
        });
        return best;
      }
      if (fallbackPrice > 0) {
        const i = candles.findIndex(
          (x) => Math.abs(x.candle.close - fallbackPrice) < 0.000001,
        );
        if (i >= 0) return i;
      }
      return -1;
    };
    const entryIndex = nearest(entryTime, entryPrice);
    const exitIndex = nearest(exitTime, exitPrice);
    if (entryIndex >= 0) {
      for (let i = entryIndex; i < candles.length; i++) {
        candles[i].actualTrade = {
          ...actualBase,
          Direction: 'LONG',
          ATR: this.num(candles[i].indicators['ATR']),
        };
      }
    }
    if (exitIndex >= 0) {
      candles[exitIndex].actualTrade = {
        ...actualBase,
        Direction: 'LONG',
        ATR: this.num(candles[exitIndex].indicators['ATR']),
      };
    }
    for (const c of candles) {
      const t = c.ticks[0];
      if (t) {
        if ((t.entryPrice ?? 0) > 0)
          c.decision['entryPrice'] = t.entryPrice ?? 0;
        if ((t.exitPrice ?? 0) > 0) c.decision['exitPrice'] = t.exitPrice ?? 0;
        if ((t.stopLoss ?? 0) > 0) c.decision['stopLoss'] = t.stopLoss ?? 0;
        if ((t.targetPrice ?? 0) > 0)
          c.decision['targetPrice'] = t.targetPrice ?? 0;
        if (t.decisionReason) c.decision['reason'] = t.decisionReason;
        if (!c.actualTrade['EntryPrice'] && (t.entryPrice ?? 0) > 0)
          c.actualTrade = {
            ...actualBase,
            EntryPrice: t.entryPrice ?? 0,
            StopLoss: t.stopLoss ?? actualBase['StopLoss'] ?? 0,
            TargetPrice: t.targetPrice ?? actualBase['TargetPrice'] ?? 0,
            EntryTime: c.timestamp,
            Direction: 'LONG',
            ATR: this.num(c.indicators['ATR']),
          };
      }
    }
    actual['Status'] =
      metadata['Status'] ?? ticks[ticks.length - 1].status ?? '';
    actual['EntryPrice'] =
      metadata['Entry Price'] ??
      ticks.find((x) => x.entryPrice! > 0)?.entryPrice ??
      0;
    actual['ExitPrice'] =
      metadata['Exit Price'] ??
      ticks.find((x) => x.exitPrice! > 0)?.exitPrice ??
      0;
    actual['ExitReason'] =
      metadata['Exit Reason'] ??
      ticks.find((x) => x.exitReason)?.exitReason ??
      '';
    actual['EstimatedNetProfit'] = metadata['Estimated Net Profit'] ?? 0;
    const symbol = String(
      metadata['Symbol'] ?? file.name.replace(/\.xlsx$/i, ''),
    );
    const token = String(
      metadata['Symbol Token'] ??
        file.name.match(/_([0-9]+)\.xlsx$/)?.[1] ??
        '',
    );
    const exchange = String(metadata['Exchange'] ?? '');
    return {
      symbol,
      token,
      exchange,
      candles,
      configuration,
      configurationSource,
      configurationCapturedAt,
    };
  }

  private parseSheet(sheet: XLSX.WorkSheet, index: number): CandleCapture {
    throw new Error(
      'Legacy candle-sheet format is no longer supported. Export the new one-sheet lifecycle workbook.',
    );
  }

  private candleIndexForTick(tick: Tick): number {
    const stock = this.stock();
    if (!stock) return -1;
    for (let i = 0; i < stock.candles.length; i++) {
      if (stock.candles[i].ticks?.some(t => t.n === tick.n && t.sequence === tick.sequence)) return i;
    }
    return -1;
  }

  private invalidateChartCache(): void {
    this.chartCacheKey = '';
    this.chartCache.clear();
    this.chartMetaCache = null;
  }

  private chartKey(): string {
    const s = this.selectedSymbol();
    const toggleKey = Object.keys(this.toggles).sort().map(k => `${k}:${this.toggles[k] ? 1 : 0}`).join('|');
    return `${s}|${this.validTicks().length}|${toggleKey}`;
  }

  private chartTicks(): Tick[] {
    const ticks = this.validTicks();
    // SVG performance degrades sharply when thousands of points are rendered.
    // Keep the full dataset for analysis/replay, but downsample only the visual layer.
    const maxPoints = 1400;
    if (ticks.length <= maxPoints) return ticks;
    const result: Tick[] = [];
    const step = (ticks.length - 1) / (maxPoints - 1);
    for (let i = 0; i < maxPoints; i++) result.push(ticks[Math.round(i * step)]);
    return result;
  }

  private ensureChartMeta(): typeof this.chartMetaCache {
    const key = this.chartKey();
    if (this.chartMetaCache?.key === key) return this.chartMetaCache;
    const ticks = this.chartTicks();
    if (!ticks.length) {
      this.chartMetaCache = { key, min: 0, max: 1, ticks: [], labels: [], markers: [] };
      return this.chartMetaCache;
    }
    const overlayKeys = this.chartOverlayKeys();
    const vals = ticks.map(t => t.ltp);
    for (const k of overlayKeys) for (const t of ticks) {
      const v = this.num(this.indicatorLookup(t.indicators, k));
      if (v !== 0) vals.push(v);
    }
    const min = Math.min(...vals), max = Math.max(...vals);
    const width = 1100, pad = 42;
    const count = Math.min(6, ticks.length);
    const indexes = Array.from({length: count}, (_, i) => count === 1 ? 0 : Math.round(i * (ticks.length - 1) / (count - 1)));
    const labels = indexes.map(i => {
      const date = this.parseTimestamp(ticks[i].utc || ticks[i].exchangeTime);
      return { x: pad + i * ((width - pad * 2) / Math.max(1, ticks.length - 1)), text: Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString('en-IN', {timeZone:'Asia/Kolkata', hour:'2-digit', minute:'2-digit'}) };
    });
    const markers = ticks.map((t, i) => ({ stage: (t.stage || '').toUpperCase(), i, t }))
      .filter(x => ['ENTRY','EXIT','REJECTED','EXPIRED'].includes(x.stage))
      .map(x => ({ x: pad + x.i * ((width - pad * 2) / Math.max(1, ticks.length - 1)), y: this.chartY(x.t.ltp, min, max, 420), stage: x.stage }));
    this.chartMetaCache = { key, min, max, ticks, labels, markers };
    return this.chartMetaCache;
  }

  private chartY(value: number, min: number, max: number, height = 420): number {
    const pad = 42, range = Math.max(0.0001, max - min);
    return height - pad - ((value - min) / range) * (height - pad * 2);
  }

  private chartLine(key: string): string {
    const cacheKey = `${this.chartKey()}|${key}`;
    const cached = this.chartCache.get(cacheKey);
    if (cached !== undefined) return cached;
    const meta = this.ensureChartMeta();
    const ticks = meta?.ticks ?? [];
    if (!ticks.length) return '';
    const width = 1100, pad = 42;
    const x = (i: number) => pad + i * ((width - pad * 2) / Math.max(1, ticks.length - 1));
    let result = '';
    for (let i = 0; i < ticks.length; i++) {
      const v = this.num(this.indicatorLookup(ticks[i].indicators, key));
      if (!Number.isFinite(v)) continue;
      const point = `${x(i).toFixed(1)},${this.chartY(v, meta!.min, meta!.max).toFixed(1)}`;
      result += (result ? ' ' : '') + point;
    }
    this.chartCache.set(cacheKey, result);
    return result;
  }

  chartTimeLabels(): Array<{ x: number; text: string }> { return this.ensureChartMeta()?.labels ?? []; }

  chartPoints(): string {
    const cacheKey = `${this.chartKey()}|PRICE`;
    const cached = this.chartCache.get(cacheKey);
    if (cached !== undefined) return cached;
    const meta = this.ensureChartMeta(), ticks = meta?.ticks ?? [];
    const width = 1100, pad = 42;
    const result = ticks.map((t, i) => `${(pad + i * ((width - pad * 2) / Math.max(1, ticks.length - 1))).toFixed(1)},${this.chartY(t.ltp, meta!.min, meta!.max).toFixed(1)}`).join(' ');
    this.chartCache.set(cacheKey, result);
    return result;
  }

  chartOverlayPoints(key: string): string { return this.chartLine(key); }


  chartXForTick(tick: Tick): number {
    const ticks = this.validTicks();
    const i = ticks.findIndex(t => t.n === tick.n && t.sequence === tick.sequence);
    return 42 + Math.max(0, i) * ((1100 - 84) / Math.max(1, ticks.length - 1));
  }

  chartYForTick(tick: Tick): number {
    const meta = this.ensureChartMeta();
    return meta?.ticks.length ? this.chartY(tick.ltp, meta.min, meta.max) : 210;
  }

  chartStageMarkers(): Array<{ x: number; y: number; stage: string }> { return this.ensureChartMeta()?.markers ?? []; }

  private value(v: unknown): string | number | boolean {
    if (typeof v === 'boolean') return v;
    if (typeof v === 'number') return v;
    if (v instanceof Date) return v.toISOString();
    const s = String(v ?? '');
    if (s === 'true' || s === 'false') return s === 'true';
    const n = Number(s);
    return s !== '' && Number.isFinite(n) ? n : s;
  }
  private iso(v: unknown): string {
    if (v instanceof Date) return v.toISOString();
    return String(v ?? '');
  }
}
