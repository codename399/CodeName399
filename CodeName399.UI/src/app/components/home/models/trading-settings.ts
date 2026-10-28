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

/** Mirrors the API TradingConfiguration JSON contract. */
export interface TradingConfiguration {
  id: string;
  enableAutoTrading: boolean;
  paperTrading: boolean;
  paperTradingAsLive: boolean;
  enableNotification: boolean;
  equity: InstrumentTradingSettings;

  riskPercentage: number;
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
  maximumStockPrice: number;
  entryType: string;
  enableSidewaysMarketFilter: boolean;
  enableChoppinessIndicator: boolean;
  choppinessPeriod: number;
  sidewaysChoppinessThreshold: number;
  chargeCoverPriceIncrement: number;
  stopLossPercent: number;
  maximumDiscoveryCandidates: number;
  buyCutoffMinutesBeforeClose: number;

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
  researchDataVersion: string;
  rawMarketData: RawMarketDataSettings;
}
