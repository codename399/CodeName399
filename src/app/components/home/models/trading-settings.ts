import { TradingStrategy } from './enum/trading-strategy';


export interface InstrumentTradingSettings {
  exchange: string;
  productType: string;
  orderType: string;
  duration: string;
  minimumPrice: number;
  minimumVolume: number;
  atrStopMultiplier: number;
  atrTargetMultiplier: number;
  maximumStopPercent: number;
  minimumStopPercent: number;
  minimumRiskReward: number;
  allowLong: boolean;
  allowShort: boolean;
  riskPercentage: number;
  maxCapitalPerTradePercent: number;
  minimumNetProfit: number;
  minimumRoiPercent: number;
  maximumChargesPerTrade: number;
  minimumConfidence: number;
  minimumFinalScore: number;
  exitOrderTimeoutSeconds: number;
  maxMarketDataAgeSeconds: number;
  maximumExitRetries: number;
  maximumSpreadPercent: number;
  maximumSpreadAmount: number;
  minimumBid: number;
  minimumAsk: number;
  maximumOpenPositions: number;
  maximumRiskPerUnderlying: number;
  maximumPositionsPerUnderlying: number;
  maximumMarginUtilizationPercent: number;
  forceSquareOffBuffer: string;
  evaluation: EvaluationSettings;
  validation: ValidationSettings;
}


export type TradingStrictnessProfile = 'VeryLoose' | 'Loose' | 'Balanced' | 'Moderate' | 'Strict' | 'VeryStrict';

export interface CoreStrategySettings {
  enabled: boolean;
  minimumCompletedCandles: number;
  minimumRecoveryScore: number;
  minimumPriceChangePercent: number;
  minimumBreakoutStrength: number;
}

export interface TradingConfiguration {
  id: string;

  equity?: InstrumentTradingSettings;

  enableAutoTrading: boolean;

  paperTrading: boolean;

  enableVirtualTrading?: boolean;

  enableNotification: boolean;

  strategy: TradingStrategy;
  dynamicEvaluation?: DynamicEvaluationSettings;
  coreStrategy?: CoreStrategySettings;
  dynamicVirtualTrading?: DynamicVirtualTradingSettings;
  futureTickPrediction?: FutureTickPredictionSettings;
  tradingStrictnessProfile?: TradingStrictnessProfile;

  riskPercentage: number;

  maxCapitalPerTradePercent: number;

  maxDailyLoss: number;

  maxDailyTrades: number;

  cooldownMinutes: number;

  ignoreMarketHours: boolean;

  marketOpenTime: string; // "09:15:00"

  marketCloseTime: string; // "15:00:00"
  intradayEntryCutoffTime?: string;
  equityMisAutoSquareOffTime?: string;
  roboAutoSquareOffTime?: string;
  casTransitionStart?: string;
  casOrderEntryStart?: string;
  casMarketOnlyEnd?: string;
  casLimitOnlyEnd?: string;
  casRandomCloseSafetyCutoff?: string;
  casEnd?: string;
  casPostCloseEnd?: string;
  casPriceBandPercent?: number;
  maxBrokerFailuresBeforeKillSwitch?: number;
  brokerFailureWindowMinutes?: number;
  maximumTotalOpenRisk?: number;
  maximumMarginUtilizationPercent?: number;
  emergencyMarginUtilizationPercent?: number;
  enableTradingKillSwitchPersistence?: boolean;
  enableGlobalRiskLimits?: boolean;
  riskReservationSeconds?: number;
  includeUnrealizedPnlInDailyLoss?: boolean;
  requireClosedHigherTimeframeCandles?: boolean;
  enablePaperMarginSimulation?: boolean;
  quoteMaxTokensPerRequest?: number;
  quoteRequestsPerSecond?: number;
  maximumSlippagePercent?: number;
  rejectDuplicateOrderIntent?: boolean;
  enableScripConsentForCashOrders?: boolean;
  orderIntentRecoveryIntervalSeconds?: number;
  orderIntentRecoveryInitialDelaySeconds?: number;
  orderIntentUnknownOrderExpiryMinutes?: number;
  webSocketHeartbeatSeconds?: number;
  webSocketPongTimeoutSeconds?: number;
  webSocketRetryInitialSeconds?: number;
  webSocketRetryMaxSeconds?: number;
  brokerPositionConfirmationDelaySeconds?: number;
  squareOffRetryDelaySeconds?: number;
  stopLossConfirmationSeconds?: number;
  capitalAllocationBaseMultiplier?: number;
  capitalAllocationConfidenceMultiplier?: number;
  eliteMovementScore?: number;
  strongMovementScore?: number;
  eliteCapitalBonus?: number;
  strongCapitalBonus?: number;
  maximumObservedDrawdownPercent?: number;
  brokerBalanceRefreshSeconds?: number;

  excludedSymbols: string[];

  watchListRefreshMinutes: number;

  minPrice: number;

  minVolume: number;

  maxCandidates: number;

  lastDailySummarySent: string | null; // e.g. "2026-07-09"
  instrumentLoadedAt?: string | null;
  marketTimeZoneId?: string;
  tradingHolidays?: string[];

  maximumChargesPerTrade: number;

  minimumRoiPercent: number;

  minimumNetProfit: number;

  enableLiveTradingPerformanceGate?: boolean;
  minimumLiveTradingPerformanceTrades?: number;
  minimumLiveTradingWinRate?: number;
  minimumLiveTradingProfitFactor?: number;
  minimumLiveTradingNetProfit?: number;
  minimumLiveTradingRiskReward?: number;
  minimumLiveTradingConfidence?: number;
  minimumRecentLiveTradingTrades?: number;
  requirePositiveRecentLiveTradingNetProfit?: boolean;
  requireBestStrategyMatchForLiveTrading?: boolean;

  // Paper-trading StockPerformance eligibility gate.
  enablePaperTradingPerformanceGate?: boolean;
  minimumPaperTradingPerformanceTrades?: number;
  minimumPaperTradingWinRate?: number;
  minimumPaperTradingProfitFactor?: number;
  minimumPaperTradingNetProfit?: number;
  minimumPaperTradingRiskReward?: number;
  minimumPaperTradingConfidence?: number;
  requireBestStrategyMatchForPaperTrading?: boolean;

  // Live-trading eligibility retention / intraday deterioration gate.
  // Optional for backward compatibility with older backend configurations.
  requireRecentPerformanceToRetainLiveTradingEligibility?: boolean;
  minimumRecentLiveTradingWinRateToRetainEligibility?: number;
  minimumRecentLiveTradingProfitFactorToRetainEligibility?: number;

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
  enableStochastic: boolean;
  stochasticPeriod: number;
  stochasticKPeriod: number;
  stochasticDPeriod: number;
  stochasticOversold: number;
  stochasticOverbought: number;
  enableAroon: boolean;
  aroonPeriod: number;
  aroonBullishThreshold: number;
  aroonBearishThreshold: number;
  enableParabolicSAR: boolean;
  parabolicSARStep: number;
  parabolicSARMaximum: number;

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
  allStockAnalysisArchive?: AllStockAnalysisArchiveSettings;
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
  ema9BelowEma21Score: number;
  ema21AboveEma50Score: number;
  superTrendBullishScore: number;
  superTrendBearishScore: number;
  priceAboveVwapScore: number;
  priceBelowVwapScore: number;
  anchoredVwapScore: number;

  strongAdxScore: number;
  mediumAdxScore: number;
  plusDiAboveMinusDiScore: number;
  minusDiAbovePlusDiScore: number;
  lowChoppinessScore: number;

  momentumIncreasingScore: number;
  pullbackIncreasingMomentumScore: number;
  lastCandleBullishScore: number;
  lastCandleBearishScore: number;
  higherHighScore: number;
  higherLowScore: number;
  lowerLowScore: number;
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

  momentumStochasticBullishScore: number;
  momentumStochasticBearishScore: number;
  pullbackStochasticBullishScore: number;
  pullbackStochasticBearishScore: number;
  momentumAroonBullishScore: number;
  momentumAroonBearishScore: number;
  pullbackAroonBullishScore: number;
  pullbackAroonBearishScore: number;
  momentumParabolicSARBullishScore: number;
  momentumParabolicSARBearishScore: number;
  pullbackParabolicSARBullishScore: number;
  pullbackParabolicSARBearishScore: number;
}

export interface ExitSettings {
  atrExitMultiplier: number;
  minimumProfitPercent: number;
  trailingActivationNetProfit?: number;
  trailingStopAtrMultiplier: number;
  trailingProfitRetentionPercent?: number;
  enableTradeFailureExit?: boolean;
  tradeFailureMinimumHoldingSeconds?: number;
  tradeFailureMinimumFavorableExcursionPercent?: number;
  tradeFailureMaximumAdverseExcursionPercent?: number;
  tradeFailureMaximumAdverseAtrMultiplier?: number;
  tradeFailureHealthScoreThreshold?: number;
  tradeFailureRequireTrendDeterioration?: boolean;
  tradeFailureRequireMomentumDeterioration?: boolean;
  tradeFailureRequireAllDeteriorationSignals?: boolean;
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
  entryMinimumPriceRatio?: number;
  maximumDrawdownPercent?: number;
  highestPriceMinimumRatio?: number;
  positiveRatioWeight?: number;
  aboveEntryRatioWeight?: number;
  maximumHigherHighBonus?: number;
  maximumConsecutivePositiveBonus?: number;
  volatilityVeryLowThreshold?: number;
  volatilityLowThreshold?: number;
  volatilityMediumThreshold?: number;
  volatilityHighThreshold?: number;
  volatilityVeryHighThreshold?: number;
  fallbackAtrPercent?: number;
  maximumPullbackGainPercent?: number;
}


export interface DynamicEvaluationSettings {
  enabled: boolean;
  minimumCandleHistory: number;
  profileLookbackCandles: number;
  minimumEntryScore: number;
  minimumQuoteOnlyEntryScore: number;
  minimumQuoteOnlySubscriptionScore: number;
  maximumQuoteOnlyRiskPenalty: number;
  maximumAdaptiveSubscriptions: number;
  historicalWarmupCandidates: number;
  strongTrendThreshold: number;
  developingThreshold: number;
  maximumEntryScore: number;
  unknownStockRiskReward: number;
  minimumRiskReward: number;
  maximumRiskReward: number;
  minimumNetProfit: number;
  poorStockNetRewardMultiplier: number;
  goodStockNetRewardMultiplier: number;
  excellentStockNetRewardMultiplier: number;
  normalStopAtrMultiplier: number;
  recoveryStopAtrMultiplier: number;
  maximumStopAtrMultiplier: number;
  maximumStructuralStopAtrDistance: number;
  targetExtensionStepAtr: number;
  maximumTargetExtensionIterations: number;
  strongPerformanceScore: number;
  excellentPerformanceScore: number;
  recoveryScoreThreshold: number;
  minimumRiskMultiplier: number;
  maximumRiskMultiplier: number;
  trendWeight: number;
  momentumWeight: number;
  candleWeight: number;
  volumeWeight: number;
  priceActionWeight: number;
  recoveryWeight: number;
  regimeWeight: number;
  multiTimeframeWeight: number;
  spreadPenaltyWeight: number;
  exhaustionPenalty: number;
  minimumExpectedNetValue: number;
  minimumEdgeScore: number;
  minimumStatisticalConfidence: number;
  noTradePenaltyThreshold: number;
  maximumRiskWhenStatisticallyUncertain: number;
  recentPerformanceWeight: number;
  historicalPerformanceWeight: number;
  marketRegimeWeight: number;
  relativeStrengthWeight: number;
}

export interface DynamicVirtualTradingSettings {
  enabled: boolean;
  minimumObservationTicks: number;
  maximumObservationTicks: number;
  minimumObservationSeconds: number;
  maximumObservationSeconds: number;
  minimumFavorableTickRatio: number;
  maximumAdverseTickRatio: number;
  recoveryTickRatioBonus: number;
  maximumAdverseMoveAtr: number;
  recoveryMaximumAdverseMoveAtr: number;
  minimumTickMomentum: number;
  minimumTrendStability: number;
  minimumMovementScore: number;
  maximumNoiseScoreForEntry: number;
  minimumRecoveryScore: number;
  minimumBreakoutStrength: number;
  minimumPriceSlope: number;
  minimumProfitAtrBeforeTrailing: number;
  baseTrailingAtrMultiplier: number;
  strongTrendTrailingAtrMultiplier: number;
  recoveryTrailingAtrMultiplier: number;
  weakTrendTrailingAtrMultiplier: number;
  minimumTrailingAtrMultiplier: number;
  maximumTrailingAtrMultiplier: number;
  minimumExecutionConfidence: number;
  peakProfitRetentionPercent: number;
  recoveryPeakProfitRetentionPercent: number;
  tickPriceMoveWeight: number;
  tickDirectionWeight: number;
  tickAccelerationWeight: number;
  tickVolumeWeight: number;
  tickSpreadWeight: number;
  tickRecoveryWeight: number;
  baseCapitalMultiplier: number;
  confidenceCapitalMultiplier: number;
}

export interface FutureTickPredictionShadowStorageSettings {
  enabled: boolean;
  path: string;
  minimumRecordIntervalSecondsPerSymbol: number;
  retentionDays: number;
}

export interface FutureTickPredictionSettings {
  enabled: boolean;
  shadowMode: boolean;
  horizonTicks: number;
  horizonSeconds: number;
  minimumFavorableProbability: number;
  useAdaptiveEntryGate: boolean;
  strongStrategyScore: number;
  adaptiveMinimumFavorableProbability: number;
  adaptiveMaximumAdverseProbability: number;
  adaptiveMinimumContinuationScore: number;
  adaptiveMinimumPredictionConfidence: number;
  maximumAdverseProbability: number;
  minimumContinuationScore: number;
  minimumPredictionConfidence: number;
  minimumExpectedNetValue: number;
  maximumAdverseMoveAtr: number;
  minimumVolumeParticipation: number;
  immediateFailureScore: number;
  minimumObservedTicksForPostEntryGate: number;
  consecutiveAdverseTicksForEarlyExit: number;
  shadowStorage: FutureTickPredictionShadowStorageSettings;
}

export interface AllStockAnalysisArchiveSettings {
  enabled: boolean;
  path: string;
  samplingIntervalSecondsPerSymbol: number;
  flushIntervalSeconds: number;
  maximumBufferedRecords: number;
  chunkMinutes: number;
  retentionDays: number;
  recordStateChanges: boolean;
  includeConfigurationSnapshot: boolean;
  recordLoadingCandidates: boolean;
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
  enableVirtualTradeTickEmails?: boolean;
  minimumVirtualTradeTicksForEmail?: number;
  virtualTradeEmailStages?: string[];
  exitPostSellTickCount?: number;
  tradeAnalyticsPersistenceIntervalSeconds?: number;
  tradeAnalysisPublicBaseUrl?: string;
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
