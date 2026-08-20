export interface TradingOptimizationStatus {
  enabled: boolean;
  mode: 'CountBased' | 'TimeBased' | 0 | 1;
  timeBasedCandidateMinutes: number;
  maximumCandidatesPerDay?: number;
  marketOpen?: boolean;
  running?: boolean;
  state?: 'DISABLED' | 'NON_TRADING_DAY' | 'WAITING_FOR_MARKET' | 'WAITING_FOR_RUNTIME' | 'RUNNING' | string;
  scheduledEndUtc?: string | null;
  remainingSeconds?: number | null;
  day: {
    date?: string;
    strategy?: number | string;
    bestCandidateId?: string | null;
    candidates?: OptimizationCandidateSummary[];
  } | null;
  candidate: OptimizationCandidateSummary | null;
}

export interface OptimizationCandidateSummary {
  candidateId: string;
  candidateNumber?: number;
  strategy?: number | string;
  startedUtc?: string;
  endedUtc?: string;
  decision?: string;
  reason?: string;
  activity?: {
    scannedSymbols?: number;
    evaluationSignals?: number;
    virtualCandidates?: number;
    virtualRejections?: number;
    virtualConfirmations?: number;
    paperTrades?: number;
  };
  metrics?: {
    completedPaperTrades?: number;
    winningTrades?: number;
    losingTrades?: number;
    winRate?: number;
    netProfit?: number;
    profitFactor?: number;
    maxDrawdownPercent?: number;
    expectancy?: number;
    meetsMinimumEvidence?: boolean;
    passesGuardrails?: boolean;
  };
}
