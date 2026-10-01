export interface InstrumentTradingSettings {
  exchange: string;
  productType: string;
  orderType: string;
  duration: string;
  atrStopMultiplier: number;
  maximumStopPercent: number;
  minimumStopPercent: number;
  allowLong: boolean;
  exitOrderTimeoutSeconds: number;
  maxMarketDataAgeSeconds: number;
  maximumExitRetries: number;
  maximumOpenPositions: number;
}

export interface ExitSettings {
  trailingStopAtrMultiplier: number;
  trailingProfitRetentionPercent: number;
}

export interface PredictionSettings {
  enabled: boolean;
  targetNetPercent: number;
  roundTripCostPercent: number;
  futureWindowSeconds: number;
  minimumHistoryTicks: number;
  predictionSpacingSeconds: number;
  maximumHistoryTicksPerSymbol: number;
  maximumSamples: number;
  minimumBuyProbability: number;
  minimumHoldProbability: number;
  liveHistoryTicks: number;
  modelFileName: string;
  trainingDataPath: string;
  trainFraction: number;
  autoTrainModel: boolean;
  autoTrainCheckIntervalMinutes: number;
  autoTrainStartupDelaySeconds: number;
  autoTrainMinimumArchiveAgeMinutes: number;
  includeCompletedTradeFeedback: boolean;
  requireValidatedModel: boolean;
  minimumRocAuc: number;
  minimumBrierSkill: number;
  minimumTrainingSamplesForLive: number;
  simulationStartingCapital: number;
  simulationMaximumRiskPerTrade: number;
  simulationMaxCapitalPerTradePercent: number;
  simulationProtectiveStopPercent: number;
}

export interface RawMarketDataSettings {
  enabled: boolean;
  storagePath: string;
  maximumZipSizeMb: number;
  rollOverAtPercent: number;
  recordOnlyDuringMarketHours: boolean;
}

/**
 * Field-for-field mirror of the API TradingConfiguration.
 * Property names intentionally match ASP.NET camelCase JSON names.
 */
export interface TradingConfiguration {
  id: string;
  enableAutoTrading: boolean;
  paperTrading: boolean;
  paperTradingAsLive: boolean;
  enableNotification: boolean;

  equity: InstrumentTradingSettings;

  riskPercentage: number;
  maxLossPercent: number;
  maxCapitalPerTradePercent: number;
  maxBrokerFailuresBeforeKillSwitch: number;
  brokerFailureWindowMinutes: number;
  ignoreMarketHours: boolean;

  marketOpenTime: string;
  marketCloseTime: string;
  intradayEntryCutoffTime: string;
  equityMisAutoSquareOffTime: string;
  roboAutoSquareOffTime: string;

  casTransitionStart: string;
  casOrderEntryStart: string;
  casMarketOnlyEnd: string;
  casLimitOnlyEnd: string;
  casRandomCloseSafetyCutoff: string;
  casEnd: string;
  casPostCloseEnd: string;
  casPriceBandPercent: number;

  watchListRefreshMinutes: number;
  excludedSymbols: string[];
  maxCandidates: number;
  lastDailySummarySent: string | null;
  instrumentLoadedAt: string | null;
  marketTimeZoneId: string;
  tradingHolidays: string[];

  maximumTotalOpenRisk: number;
  riskReservationSeconds: number;
  enableEntryExecutionAudit: boolean;
  enableScripConsentForCashOrders: boolean;
  webSocketHeartbeatSeconds: number;
  webSocketPongTimeoutSeconds: number;
  webSocketRetryInitialSeconds: number;
  webSocketRetryMaxSeconds: number;
  brokerPositionConfirmationDelaySeconds: number;
  squareOffRetryDelaySeconds: number;
  stopLossConfirmationSeconds: number;

  buyTradingInterval: number;
  sellTradingInterval: number;
  visibleColumns: string[];

  autoSquareOff: boolean;
  paperTradingBalance: number;

  researchDataVersion: string;
  exit: ExitSettings;
  prediction: PredictionSettings;
  rawMarketData: RawMarketDataSettings;
}
