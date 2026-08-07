import { TradingStrategy } from './enum/trading-strategy';

export interface TradingConfiguration {
  id: string;

  enableAutoTrading: boolean;

  paperTrading: boolean;

  enableNotification: boolean;

  strategy: TradingStrategy;

  riskPercentage: number;

  maxCapitalPerTrade: number;

  maxDailyLoss: number;

  maxDailyTrades: number;

  cooldownMinutes: number;

  ignoreMarketHours: boolean;

  marketOpenTime: string; // "09:15:00"

  marketCloseTime: string; // "15:00:00"

  excludedSymbols: string[];

  watchListRefreshMinutes: number;

  minPrice: number;

  minVolume: number;

  maxCandidates: number;

  lastDailySummarySent: string | null; // e.g. "2026-07-09"

  maximumChargesPerTrade: number;

  minimumRoiPercent: number;

  minimumNetProfit: number;

  autoSquareOff: boolean;

  paperTradingBalance: number;

  enableEMA9: boolean;

  enableEMA21: boolean;

  enableEMA50: boolean;

  enableEMA200: boolean;

  enableATR: boolean;

  enableRSI: boolean;

  enableVWAP: boolean;

  enableADX: boolean;

  enableRelativeVolume: boolean;

  enableEMASlope: boolean;

  enableDistanceFromEMA: boolean;

  enableChoppiness: boolean;

  enableSuperTrend: boolean;

  enableAnchoredVWAP: boolean;

  enableMACD: boolean;

  enableBollinger: boolean;

  virtualTradeObservationSeconds: number;

  virtualTradeExpirySeconds: number;

  minimumVirtualProfitPercent: number;

  maximumVirtualPullbackPercent: number;

  buyTradingInterval: number;

  sellTradingInterval: number;

  visibleColumns?: string[];

  validation: ValidationSettings;

  evaluation: EvaluationSettings;

  virtualTrading: VirtualTradingSettings;

  exit: ExitSettings;
}

export interface EvaluationSettings {
  strongAdx: number;
  mediumAdx: number;
  lowChoppiness: number;
  highChoppiness: number;
  highRelativeVolume: number;
  mediumRelativeVolume: number;
  lowRelativeVolume: number;
  excellentScore: number;
  goodScore: number;
  averageScore: number;
}

export interface ExitSettings {
  atrExitMultiplier: number;
  minimumProfitPercent: number;
}

export interface ValidationSettings {
  minimumMovementScore: number;
  minimumConfidence: number;
  minimumRiskReward: number;
  minimumTrendStrength: number;
  minimumTrendStability: number;
  minimumRecoveryScore: number;
  minimumVolatilityScore: number;
  minimumNoiseScore: number;
  minimumBreakoutStrength: number;
  minimumRSI: number;
  maximumRSI: number;
  minimumVolumeMultiplier: number;
  minimumPositiveTickRatio: number;
  minimumAboveEntryRatio: number;
  minimumHigherHighs: number;
  minimumConsecutivePositiveTicks: number;
  maximumConsecutiveNegativeTicks: number;
  maximumDrawdownPercent: number;
}

export interface VirtualTradingSettings {
  warmupSeconds: number;
  observationSeconds: number;
  maximumObservationSeconds: number;
  tickWindow: number;
  entryLossPercent: number;
  highestPriceTolerance: number;
}
