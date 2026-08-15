export interface TradingOptimizationStatus {
  enabled: boolean;
  mode: 'CountBased' | 'TimeBased' | 0 | 1;
  timeBasedCandidateMinutes: number;
  maximumCandidatesPerDay?: number;
  marketOpen?: boolean;
  running?: boolean;
  scheduledEndUtc?: string | null;
  remainingSeconds?: number | null;
  day: OptimizationDayReport | null;
  candidate: OptimizationCandidateReport | null;
}

export interface OptimizationDayReport {
  date: string;
  startedUtc: string;
  endedUtc?: string | null;
  strategy: number | string;
  status: string;
  bestCandidateId: string;
  candidates: OptimizationCandidateReport[];
}

export interface OptimizationCandidateReport {
  candidateId: string;
  candidateNumber: number;
  strategy: number | string;
  startedUtc: string;
  endedUtc?: string | null;
  decision: string;
  reason: string;
  activity: OptimizationTelemetrySnapshot;
  metrics: PaperOptimizationMetrics;
  changes: ConfigurationChange[];
  configurationJson: string;
  trades: unknown[];
  logExcerpt: string;
}

export interface OptimizationTelemetrySnapshot {
  evaluationSignals: number;
  virtualCandidates: number;
  virtualConfirmations: number;
  paperTrades: number;
}

export interface PaperOptimizationMetrics {
  completedPaperTrades: number;
  winningTrades: number;
  losingTrades: number;
  winRate: number;
  grossProfit: number;
  charges: number;
  netProfit: number;
  maxDrawdown: number;
  maxDrawdownPercent: number;
  profitFactor: number;
  expectancy: number;
  meetsMinimumEvidence: boolean;
  passesGuardrails: boolean;
}

export interface ConfigurationChange {
  path: string;
  oldValue: string;
  newValue: string;
}
