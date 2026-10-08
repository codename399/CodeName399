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
  riskRewardRatio: number;
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
  minimumPriceChangePercent: number;
  minimumVolume: number;
  requireRisingFromDayOpen: boolean;
  candidateObservationSeconds?: number;
  candidateObservationTicks?: number;
  minimumSteadyProgressPercent: number;
  minimumSteadyPositiveTickRatio: number;
  maximumSteadyPullbackPercent: number;
  minimumFluctuationRangePercent: number;
  minimumFluctuationPullbackPercent: number;
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

  exit: ExitSettings;
  rawMarketData: RawMarketDataSettings;
}
