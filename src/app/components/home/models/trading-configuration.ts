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

  confidence: ConfidenceSettings;

  reporting: ReportingSettings;
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

  ema9AboveEma21Score: number;
  ema21AboveEma50Score: number;
  superTrendBullishScore: number;
  priceAboveVwapScore: number;
  anchoredVwapScore: number;

  strongAdxScore: number;
  mediumAdxScore: number;
  plusDiAboveMinusDiScore: number;
  lowChoppinessScore: number;

  momentumIncreasingScore: number;
  lastCandleBullishScore: number;
  higherHighScore: number;
  higherLowScore: number;
  ema9SlopePositiveScore: number;
  ema21SlopePositiveScore: number;

  highRelativeVolumeScore: number;
  mediumRelativeVolumeScore: number;
  lowRelativeVolumeScore: number;

  pullbackHighRelativeVolumeScore: number;
  pullbackMediumRelativeVolumeScore: number;
  pullbackLowRelativeVolumeScore: number;

  momentumPullbackIdealMinimum: number;
  momentumPullbackIdealMaximum: number;
  momentumPullbackMaximum: number;
  momentumPullbackIdealScore: number;
  momentumPullbackSecondaryScore: number;

  pullbackDistanceMinimum: number;
  pullbackDistanceIdealMaximum: number;
  pullbackDistanceMaximum: number;
  pullbackDistanceIdealScore: number;
  pullbackDistanceSecondaryScore: number;
  ema9BounceScore: number;
  vwapBounceScore: number;

  rsiValidScore: number;
  rsiAboveMinimumScore: number;
  rsiBelowMinimumPenalty: number;
  momentumRsiBelowMinimumPenalty: number;

  macdBullishScore: number;
  macdBearishPenalty: number;
  macdHistogramPositiveScore: number;
  macdHistogramNegativePenalty: number;

  momentumWideBollingerBandwidth: number;
  momentumMediumBollingerBandwidth: number;
  momentumNarrowBollingerBandwidth: number;
  momentumWideBollingerScore: number;
  momentumMediumBollingerScore: number;
  momentumNarrowBollingerPenalty: number;

  pullbackWideBollingerBandwidth: number;
  pullbackNarrowBollingerBandwidth: number;
  pullbackWideBollingerScore: number;
  pullbackNarrowBollingerPenalty: number;

  momentumExhaustedMovePenalty: number;
  pullbackExhaustedMovePenalty: number;
  superTrendBearishPenalty: number;
  highChoppinessPenalty: number;
}

export interface ExitSettings {
  atrExitMultiplier: number;
  minimumProfitPercent: number;
  trailingStopAtrMultiplier: number;
}

// ValidationSettings is defined later with extended fields.

export interface VirtualTradingSettings {
  warmupSeconds: number;
  observationSeconds: number;
  maximumObservationSeconds: number;
  tickWindow: number;
  entryLossPercent: number;
  highestPriceTolerance: number;
  tradeExpirySeconds?: number;
  minimumObservationForTrendSeconds?: number;
  confidenceBonusAfterSeconds1?: number;
  confidenceBonusAfterSeconds2?: number;
  pullbackWarmupSeconds?: number;
}

export interface ReportingSettings {
  strongBuyConfidence: number;
  buyConfidence: number;
  watchConfidence: number;
  movementScoreThreshold: number;
  trendStrengthThreshold: number;
  trendStabilityThreshold: number;
  recoveryScoreThreshold: number;
  higherHighCountThreshold: number;
  volumeMultiplierThreshold: number;
  maxDrawdownPercentThreshold: number;
  volatilityScoreThreshold: number;
  riskRewardThreshold: number;
}

export interface ConfidenceSettings {
  maximumGainBonus: number;
  gainBonusMultiplier: number;
  strongPositiveTickRatio: number;
  mediumPositiveTickRatio: number;
  strongTickBonus: number;
  mediumTickBonus: number;
  weakTickBonus: number;
  maximumPositiveStreakBonus: number;
  maximumHigherHighBonus: number;
  drawdownPenaltyMultiplier: number;
  idealMinimumRSI: number;
  idealMaximumRSI: number;
  overboughtRSI: number;
  oversoldRSI: number;
  macdBullishBonus: number;
  macdBearishPenalty: number;
  macdHistogramBonus: number;
  macdHistogramPenalty: number;
  highVolumeMultiplier: number;
  mediumVolumeMultiplier: number;
  lowVolumeMultiplier: number;
  highVolumeBonus: number;
  mediumVolumeBonus: number;
  lowVolumeBonus: number;
  wideBandwidth: number;
  narrowBandwidth: number;
  wideBandwidthBonus: number;
  narrowBandwidthPenalty: number;
  observationBonus1: number;
  observationBonus2: number;
  movementWeight: number;
  trendStrengthWeight: number;
  trendStabilityWeight: number;
  recoveryWeight: number;
  breakoutWeight: number;
}

// Extend ValidationSettings with additional thresholds migrated from backend
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

  minimumRejectMovementScore?: number;
  maximumRejectDrawdownPercent?: number;
  minimumRejectPositiveTickRatio?: number;
  minimumRejectAboveEntryRatio?: number;
  maximumRejectConsecutiveNegativeTicks?: number;
  minimumRejectProfitPercent?: number;
  minimumGainPercent?: number;
  minimumPriceSlope?: number;

  momentumMinimumPriceRatio?: number;
  momentumMaximumDrawdown?: number;
  momentumHighestPriceTolerance?: number;

  pullbackMinimumPriceRatio?: number;
  maximumPullbackGain?: number;

  minimumFinalScore?: number;
  minimumBollingerBandwidth?: number;
  minimumFinalRSI?: number;
  maximumFinalRSI?: number;
}
