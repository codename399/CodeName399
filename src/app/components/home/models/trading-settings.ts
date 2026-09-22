export interface CoreStrategySettings {
  minimumCompletedCandles: number;
  minimumRecoveryScore: number;
  minimumPriceChangePercent: number;
  minimumBreakoutStrength: number;
}

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

export interface TradingConfiguration {
  id: string;
  enableAutoTrading: boolean;
  paperTrading: boolean;
  enableNotification: boolean;

  coreStrategy: CoreStrategySettings;
  equity: InstrumentTradingSettings;

  riskPercentage: number;
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

  lastDailySummarySent?: string | null;
  instrumentLoadedAt?: string | null;
  marketTimeZoneId: string;
  tradingHolidays: string[];

  maximumTotalOpenRisk: number;
  enableTradingKillSwitchPersistence: boolean;
  riskReservationSeconds: number;

  enableEntryExecutionAudit: boolean;
  entryExecutionAuditPostFillTickCount: number;
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
  brokerBalanceRefreshSeconds: number;

  exit: ExitSettings;
}
