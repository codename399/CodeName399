import { CommonModule } from '@angular/common';
import { Component, computed, signal, inject } from '@angular/core';
import { Router } from '@angular/router';
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
export class TradingSimulationComponent {
  private readonly router = inject(Router);

  returnToDashboard(): void {
    void this.router.navigate(['/home/angel-one']);
  }

  stocks = signal<StockCapture[]>([]);
  selectedSymbol = signal('');
  selectedCandle = signal(0);
  playing = signal(false);
  playIndex = signal(0);
  replayTick = signal<Tick | null>(null);
  replayStatus = signal('Ready');
  actionBusy = signal(false);
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
  indicators = computed(() => this.candle()?.indicators ?? {});
  decision = computed(() => this.candle()?.decision ?? {});
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
      this.selectedCandle.set(0);
      this.loadParametersFromConfiguration(loaded[0].configuration);
      this.runSimulation();
    }
  }
  selectStock(symbol: string): void {
    this.selectedSymbol.set(symbol);
    this.selectedCandle.set(0);
    this.playIndex.set(0);
    const s = this.stocks().find((x) => x.symbol === symbol);
    if (s) this.loadParametersFromConfiguration(s.configuration);
    this.runSimulation();
  }
  selectCandle(index: number): void {
    const stock = this.stock();
    const safeIndex = stock?.candles?.length
      ? Math.max(0, Math.min(index, stock.candles.length - 1))
      : 0;
    this.selectedCandle.set(safeIndex);
    this.playIndex.set(0);
    this.refreshSelectedDiagnostics();
  }
  toggle(name: string): void {
    this.toggles[name] = !this.toggles[name];
  }
  async runLive(): Promise<void> {
    const stock = this.stock();
    if (!stock) {
      this.replayStatus.set('Upload a lifecycle workbook first.');
      return;
    }
    const ticks = stock.candles
      .flatMap((c) => c.ticks)
      .filter((t) => t.ltp > 0)
      .sort((a, b) => this.time(a) - this.time(b));
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
      const candleIndex = stock.candles.findIndex((c) =>
        c.ticks.some((t) => t.n === tick.n && t.sequence === tick.sequence),
      );
      if (candleIndex >= 0) this.selectedCandle.set(candleIndex);
      await new Promise((resolve) => setTimeout(resolve, 80));
    }
    const finished = this.playIndex() >= ticks.length;
    this.playing.set(false);
    this.replayStatus.set(
      finished
        ? `Replay complete — ${ticks.length} ticks evaluated.`
        : `Paused at tick ${this.playIndex()} of ${ticks.length}.`,
    );
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
    this.actionStatus.set('Running simulation…');
    setTimeout(() => {
      try {
        this.runSimulation();
        this.activeTab.set('simulation');
        this.actionStatus.set(
          `Simulation complete — ${this.simulation().trades} accepted trade(s), ${this.simulation().missed} missed opportunity(ies).`,
        );
      } catch (e) {
        this.error.set(e instanceof Error ? e.message : 'Simulation failed.');
        this.actionStatus.set('Simulation failed.');
      } finally {
        this.actionBusy.set(false);
      }
    }, 0);
  }

  optimizeFromUi(): void {
    this.runLongAction(
      'Finding the best configuration for the selected stock…',
      () => this.optimize(),
    );
  }

  optimizeGlobalFromUi(): void {
    this.runLongAction(
      'Searching for a global configuration across all stocks…',
      () => this.optimizeGlobal(),
    );
  }

  learnGlobalFromUi(): void {
    this.runLongAction(
      'Learning global dynamics from captured decisions…',
      () => this.learnGlobalDynamics(),
    );
  }

  learnRegimeFromUi(): void {
    this.runLongAction('Learning regime-specific policies…', () =>
      this.learnRegimeDynamics(),
    );
  }

  private runLongAction(message: string, action: () => void): void {
    if (!this.stock()) {
      this.replayStatus.set(
        'Upload a lifecycle workbook before starting this operation.',
      );
      return;
    }
    this.actionBusy.set(true);
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

    candles.forEach((c) => {
      const d = c.decision;
      const p = c.candle.close;
      const score = this.num(d['score']);
      const conf = this.num(d['adaptiveConfidence']);
      const rr = this.num(d['adaptiveRiskReward']);
      const edge = this.num(d['adaptiveEdgeScore']);
      const spread = this.num(
        this.indicatorLookup(c.indicators, 'SpreadPercent') ??
          c.ticks[0]?.spreadPct,
      );
      const technicalBuy = String(d['signal'] ?? '').toUpperCase() === 'BUY';
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
        missed++;
        missedProfit += (p * netPct) / 100;
        const gate = gateList[0] ?? 'Configuration gate';
        reasons.set(gate, (reasons.get(gate) ?? 0) + 1);
        issues.push({
          kind: 'MISSED_ENTRY',
          timestamp: c.timestamp,
          title: `Missed BUY at ${this.format(c.timestamp)}`,
          detail: `The captured move reached +${gainPct.toFixed(2)}% after this candle, but the setup was blocked by ${gate}.`,
          severity: gainPct >= 1 ? 'high' : 'medium',
          metric: gate,
          current: this.gateValue(gate, score, conf, rr, edge, spread),
          suggested: this.gateSuggestion(gate, score, conf, rr, edge, spread),
        });
        this.recordRecommendation(recommendationEvidence, gate, gainPct);
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
          issues.push({
            kind: 'EXIT',
            timestamp: c.timestamp,
            title: `Exit opportunity after ${this.format(c.timestamp)}`,
            detail: `Price subsequently reached ₹${bestExitTick.ltp.toFixed(2)} before the observed decline.`,
            severity: 'medium',
            metric: 'Exit price',
            current: exit,
            suggested: bestExitTick.ltp,
          });
        }
      }
      void futureWorst;
      void lossPct;
    });
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

  downloadConfigurationProposal(): void {
    const p = this.configurationProposal();
    if (!p) return;
    const blob = new Blob([JSON.stringify(p, null, 2)], {
      type: 'application/json',
    });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${p.proposalId}.json`;
    a.click();
    URL.revokeObjectURL(url);
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
      v: any = c.virtualTrade;
    const atr = this.num(i['ATR'] ?? i['atr']);
    const close = c.candle.close;
    const atrPct = close > 0 ? (atr / close) * 100 : 0;
    const trend = this.num(
      v['trendStrength'] ?? v['TrendStrength'] ?? d['trendStrength'],
    );
    const stability = this.num(v['trendStability'] ?? v['TrendStability']);
    const breakout = this.num(v['breakoutStrength'] ?? d['breakoutStrength']);
    const recovery = this.num(v['recoveryScore'] ?? d['recoveryScore']);
    const slope = Math.abs(this.num(v['priceSlope'] ?? d['priceSlope']));
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
    const d = c.decision,
      v = c.virtualTrade,
      cfg: any = stock.configuration ?? {};
    const dynamic: any =
      cfg['dynamicVirtualTrading'] ?? cfg['DynamicVirtualTrading'] ?? {};
    const evalCfg: any =
      cfg['dynamicEvaluation'] ?? cfg['DynamicEvaluation'] ?? {};
    const short = String(d['direction'] ?? v['direction'] ?? 'LONG')
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
    const observationTicks = n(v['tickCount']);
    const minTicks = n(
      dynamic['minimumObservationTicks'] ?? dynamic['MinimumObservationTicks'],
    );
    const minSeconds = n(
      dynamic['minimumObservationSeconds'] ??
        dynamic['MinimumObservationSeconds'],
    );
    const maxTicks = n(
      v['maximumObservationTicks'] ?? d['adaptiveMaximumObservationTicks'],
    );
    const tickSpan = this.tickSpanSeconds(c.ticks);
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
    const maxObsTicks = n(v['maximumObservationTicks']);
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
    const quality = n(v['tickQualityScore']);
    const minQuality = n(
      dynamic['minimumExecutionConfidence'] ??
        dynamic['MinimumExecutionConfidence'],
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
    const noise = n(v['noiseScore']);
    const maxNoise = n(
      dynamic['maximumNoiseScoreForEntry'] ??
        dynamic['MaximumNoiseScoreForEntry'],
    );
    const noiseRelax = quality >= minQuality + 10;
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
    const movement = n(v['movementScore']);
    const policyConfidence = n(
      d['adaptiveConfidence'] ?? v['adaptiveConfidence'],
    );
    const movementFloor = Math.max(
      20,
      n(dynamic['minimumMovementScore'] ?? dynamic['MinimumMovementScore']) -
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
    const stability = n(v['trendStability']);
    const recoveryProtected =
      String(
        d['adaptiveRecoveryProtected'] ?? v['recoveryProtected'],
      ).toLowerCase() === 'true';
    const stabilityFloor = Math.max(
      30,
      n(dynamic['minimumTrendStability'] ?? dynamic['MinimumTrendStability']) -
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
    const breakout = n(v['breakoutStrength']),
      recovery = n(v['recoveryScore']),
      minBreak = n(
        dynamic['minimumBreakoutStrength'] ??
          dynamic['MinimumBreakoutStrength'],
      ),
      minRec = n(
        dynamic['minimumRecoveryScore'] ?? dynamic['MinimumRecoveryScore'],
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
    const slope = n(v['priceSlope']),
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
    const noTrade = n(d['adaptiveNoTradeScore'] ?? v['noTradeScore']),
      noTradeThreshold = n(
        evalCfg['noTradePenaltyThreshold'] ??
          evalCfg['NoTradePenaltyThreshold'],
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
    const expected = n(d['adaptiveExpectedNetValue'] ?? v['expectedNetValue']),
      minExpected = n(
        evalCfg['minimumExpectedNetValue'] ??
          evalCfg['MinimumExpectedNetValue'],
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
    const edge = n(d['adaptiveEdgeScore'] ?? v['edgeScore']),
      stat = n(
        d['adaptiveStatisticalConfidence'] ?? v['statisticalConfidence'],
      ),
      minEdge = n(evalCfg['minimumEdgeScore'] ?? evalCfg['MinimumEdgeScore']),
      minStat = n(
        evalCfg['minimumStatisticalConfidence'] ??
          evalCfg['MinimumStatisticalConfidence'],
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
    const favorable = short ? n(v['negativeTicks']) : n(v['positiveTicks']),
      adverse = short ? n(v['positiveTicks']) : n(v['negativeTicks']),
      total = Math.max(
        1,
        n(v['positiveTicks']) + n(v['negativeTicks']) + n(v['flatTicks']),
      );
    const favRatio = favorable / total,
      advRatio = adverse / total;
    const minFav = n(
        d['adaptiveMinimumFavorableTickRatio'] ??
          v['minimumFavorableTickRatio'],
      ),
      maxAdv = n(
        d['adaptiveMaximumAdverseTickRatio'] ?? v['maximumAdverseTickRatio'],
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
    const signal = String(d['signal'] ?? 'HOLD').toUpperCase();
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
    const entry = this.num(t['EntryPrice']);
    const exit = this.num(t['ExitPrice']);
    const direction = String(
      t['Direction'] ?? c.decision['direction'] ?? 'LONG',
    ).toUpperCase();
    const short = direction.includes('SHORT');
    const entryTime = new Date(String(t['EntryTime'] ?? c.timestamp)).getTime();
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
    const atr =
      this.num(t['ATR']) || this.num(c.indicators['ATR']) || entry * 0.005;
    const stop = this.num(t['StopLoss']) || this.num(c.decision['stopLoss']);
    const target =
      this.num(t['TargetPrice']) || this.num(c.decision['targetPrice']);
    const trailingMultiplier =
      this.num(t['AdaptiveTrailingAtrMultiplier']) ||
      this.num(policy['TrailingAtrMultiplier']) ||
      this.num(
        ex['TrailingStopAtrMultiplier'] ?? ex['trailingStopAtrMultiplier'],
      );
    const activation =
      this.num(t['TrailingActivationNetProfit']) ||
      this.num(
        ex['TrailingActivationNetProfit'] ?? ex['trailingActivationNetProfit'],
      );
    const retention =
      this.num(t['PeakProfitRetentionPercent']) ||
      this.num(
        ex['TrailingProfitRetentionPercent'] ??
          ex['trailingProfitRetentionPercent'],
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
              this.time(x) >= new Date(String(t['ExitTime'] ?? '')).getTime(),
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
      t = c?.ticks?.[0];
    return (
      this.indicatorLookup(t?.indicators, key) ??
      this.indicatorLookup(c?.indicators, key)
    );
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
        this.indicatorLookup(c.ticks?.[0]?.indicators, name);
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
    return new Date(t.exchangeTime || t.utc).getTime();
  }
  num(v: unknown): number {
    const n = Number(v);
    return Number.isFinite(n) ? n : 0;
  }
  private format(v: string): string {
    const d = new Date(v);
    return Number.isNaN(d.getTime())
      ? v
      : d.toLocaleTimeString([], {
          hour: '2-digit',
          minute: '2-digit',
          second: '2-digit',
        });
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

  chartPoints(): string {
    const ticks =
      this.stock()
        ?.candles.flatMap((c) => c.ticks)
        .filter((t) => t.ltp > 0) ?? [];
    if (!ticks.length) return '';
    const vals = ticks
      .map((t) => t.ltp)
      .concat(
        this.chartOverlayKeys().flatMap((k) =>
          ticks
            .map((t) => this.num(this.indicatorLookup(t.indicators, k)))
            .filter((v) => v !== 0),
        ),
      );
    const min = Math.min(...vals),
      max = Math.max(...vals),
      range = Math.max(0.0001, max - min),
      width = 1100,
      height = 420,
      pad = 42;
    const x = (i: number) =>
      pad + i * ((width - pad * 2) / Math.max(1, ticks.length - 1));
    const y = (v: number) =>
      height - pad - ((v - min) / range) * (height - pad * 2);
    return ticks
      .map((t, i) => `${x(i).toFixed(1)},${y(t.ltp).toFixed(1)}`)
      .join(' ');
  }
  chartOverlayPoints(key: string): string {
    return this.chartIndicatorPoints(key);
  }
  chartXForTick(tick: Tick): number {
    const ticks =
      this.stock()
        ?.candles.flatMap((c) => c.ticks)
        .filter((t) => t.ltp > 0) ?? [];
    const i = ticks.findIndex(
      (t) => t.n === tick.n && t.sequence === tick.sequence,
    );
    return 42 + Math.max(0, i) * ((1100 - 84) / Math.max(1, ticks.length - 1));
  }
  chartYForTick(tick: Tick): number {
    const ticks =
      this.stock()
        ?.candles.flatMap((c) => c.ticks)
        .filter((t) => t.ltp > 0) ?? [];
    if (!ticks.length) return 210;
    const vals = ticks
      .map((t) => t.ltp)
      .concat(
        this.chartOverlayKeys().flatMap((k) =>
          ticks
            .map((t) => this.num(this.indicatorLookup(t.indicators, k)))
            .filter((v) => v !== 0),
        ),
      );
    const min = Math.min(...vals),
      max = Math.max(...vals),
      range = Math.max(0.0001, max - min);
    return 420 - 42 - ((tick.ltp - min) / range) * (420 - 84);
  }
  chartStageMarkers(): Array<{ x: number; y: number; stage: string }> {
    const ticks =
      this.stock()
        ?.candles.flatMap((c) => c.ticks)
        .filter((t) => t.ltp > 0) ?? [];
    return ticks
      .map((t) => ({
        x: this.chartXForTick(t),
        y: this.chartYForTick(t),
        stage: (t.stage || '').toUpperCase(),
      }))
      .filter((m) =>
        ['ENTRY', 'EXIT', 'REJECTED', 'EXPIRED'].includes(m.stage),
      );
  }
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
