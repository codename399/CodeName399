export interface Tick {
  n: number; utc: string; exchangeTime: string; sequence: number; ltp: number; bid: number; ask: number;
  spread: number; spreadPct: number; open: number; high: number; low: number; close: number;
  ltq: number; avgPrice: number; dayVolume: number; buyQty: number; sellQty: number;
}

export interface Indicators { [key: string]: number | boolean | string; }
export interface ActualTrade { [key: string]: number | boolean | string; }

export interface CandleCapture {
  index: number; timestamp: string;
  candle: { timestamp: string; open: number; high: number; low: number; close: number; volume: number };
  indicators: Indicators;
  decision: Record<string, string | number | boolean>;
  virtualTrade: Record<string, string | number | boolean>;
  actualTrade: ActualTrade;
  ticks: Tick[];
}

export interface StockCapture { symbol: string; token: string; exchange: string; candles: CandleCapture[]; configuration: Record<string, unknown>; }

export interface DiagnosticIssue {
  kind: 'MISSED_ENTRY'|'LOSING_TRADE'|'EXIT'|'THRESHOLD';
  timestamp: string;
  title: string;
  detail: string;
  severity: 'high'|'medium'|'low';
  metric?: string;
  current?: number;
  suggested?: number;
}

export interface ConfigurationRecommendation {
  parameter: string;
  current: number;
  suggested: number;
  reason: string;
  evidenceCount: number;
  expectedImprovement: number;
}

export interface FuturePathAnalysis {
  entryTime: string; exitTime?: string; entryPrice: number; actualExitPrice?: number;
  maxPrice: number; minPrice: number; bestExitPrice: number; bestExitTime: string;
  maxFavorablePercent: number; maxAdversePercent: number; realizedPercent: number;
  leftOnTablePercent: number; diagnosis: string;
}

export interface GateEvaluation { name: string; status: 'PASS'|'FAIL'|'UNKNOWN'; actual?: number; required?: number; suggested?: number; margin?: number; reason: string; source: 'captured'|'replayed'|'unavailable'; }

export interface ReplayDecision {
  signal: string; shouldBuy: boolean; gates: GateEvaluation[]; firstBlockingGate?: string;
  entryMistake?: string; exitMistake?: string; futureBestPrice?: number; futureWorstPrice?: number;
  productionParity: 'FULL'|'PARTIAL'|'UNKNOWN'; parityNotes: string[];
  exitReplay?: ExitReplay;
  regime?: string;
  appliedPolicy?: RegimeConfiguration['configuration'];
  policySource?: 'BASELINE'|'REGIME_LEARNED'|'COUNTERFACTUAL';
  policyImpact?: string;
}
export interface ExitReplay {
  status: 'OPEN'|'EXITED'|'NO_EXIT_DATA'; direction: string; entryPrice: number; observedExitPrice?: number;
  observedExitReason?: string; gates: GateEvaluation[]; firstBlockingGate?: string;
  bestFutureExitPrice?: number; bestFutureExitTime?: string; worstFuturePrice?: number;
  prematureExit: boolean; stopWouldHaveTriggered?: boolean; targetWouldHaveTriggered?: boolean;
  trailingWouldHaveActivated?: boolean; diagnosis: string;
}

export interface GlobalConfigurationResult {
  stocks: number; observations: number; profitableOpportunities: number;
  currentNetProfit: number; optimizedNetProfit: number; improvement: number;
  currentMissed: number; optimizedMissed: number; currentLosses: number; optimizedLosses: number;
  configuration: { minimumScore: number; minimumConfidence: number; minimumRiskReward: number; minimumEdgeScore: number; maximumSpreadPercent: number; minimumProfitPercent: number };
  confidence: 'LOW'|'MEDIUM'|'HIGH'; rationale: string;
  validation: { trainObservations:number; testObservations:number; trainImprovement:number; testImprovement:number; coveragePercent:number; maxLossIncrease:number; rollbackRule:string };
  proposalId?: string;
}

export interface GlobalLearningResult {
  proposalId: string;
  baseline: { netProfit:number; missed:number; losses:number };
  recommended: { minimumScore:number; minimumConfidence:number; minimumRiskReward:number; minimumEdgeScore:number; maximumSpreadPercent:number; minimumProfitPercent:number };
  holdout: { netProfit:number; improvement:number; missed:number; losses:number; observations:number };
  parameterEvidence: Array<{ parameter:string; direction:'LOWER'|'RAISE'; current:number; learned:number; evidence:number; impact:number; confidence:'LOW'|'MEDIUM'|'HIGH' }>;
  robustness: { stockCoveragePercent:number; positiveHoldoutStocks:number; totalHoldoutStocks:number; worstStockImprovement:number; maxLossIncrease:number };
  rationale: string;
}


export interface RegimeConfiguration {
  regime: 'TRENDING'|'RANGING'|'HIGH_VOLATILITY'|'LOW_VOLATILITY'|'BREAKOUT'|'RECOVERY'|'UNKNOWN';
  observations: number;
  stocks: number;
  baselineNetProfit: number;
  optimizedNetProfit: number;
  improvement: number;
  missed: number;
  losses: number;
  configuration: { minimumScore:number; minimumConfidence:number; minimumRiskReward:number; minimumEdgeScore:number; maximumSpreadPercent:number };
  confidence: 'LOW'|'MEDIUM'|'HIGH';
  rationale: string;
}

export interface RegimeLearningResult {
  proposalId: string;
  regimes: RegimeConfiguration[];
  defaultRegime: string;
  regimeCoveragePercent: number;
  holdoutImprovement: number;
  maxLossIncrease: number;
  rationale: string;
}

export interface SimulationResult {
  netProfit: number; trades: number; wins: number; losses: number; missed: number;
  actualLosses: number; avoidableLosses: number; missedProfit: number;
  bestEntry: number; bestExit: number; bestEntryTime: string; bestExitTime: string;
  opportunityPercent: number; reasons: string[]; recommendation: string;
  issues: DiagnosticIssue[]; recommendations: ConfigurationRecommendation[]; replay?: ReplayDecision; futurePath?: FuturePathAnalysis; futurePaths?: FuturePathAnalysis[]; globalConfiguration?: GlobalConfigurationResult;
}

export interface DecisionDebugStep {
  order: number;
  phase: 'SIGNAL'|'GATE'|'ENTRY'|'FUTURE'|'EXIT'|'OUTCOME';
  time: string;
  status: 'PASS'|'FAIL'|'INFO'|'UNKNOWN';
  title: string;
  detail: string;
  parameter?: string;
  current?: number;
  suggested?: number;
  impact?: string;
}

export interface ConfigurationProposal {
  proposalId: string;
  version: number;
  createdAt: string;
  status: 'PROPOSED'|'VALIDATED'|'REJECTED'|'ROLLED_BACK';
  scope: 'GLOBAL'|'REGIME_AWARE';
  baseline: { minimumScore:number; minimumConfidence:number; minimumRiskReward:number; minimumEdgeScore:number; maximumSpreadPercent:number; minimumProfitPercent:number };
  recommended: { minimumScore:number; minimumConfidence:number; minimumRiskReward:number; minimumEdgeScore:number; maximumSpreadPercent:number; minimumProfitPercent:number };
  validation: { trainImprovement:number; holdoutImprovement:number; holdoutLossChange:number; stockCoveragePercent:number; positiveHoldoutStocks:number; totalHoldoutStocks:number };
  changes: Array<{parameter:string; from:number; to:number; evidence:number; expectedImpact:number; confidence:'LOW'|'MEDIUM'|'HIGH'}>;
  regimePolicies: RegimeConfiguration[];
  rollback: { trigger:string; baselineNetProfit:number; minimumAllowedImprovement:number; maximumAllowedLossIncrease:number; evaluationWindowDays:number };
  rationale: string;
}
