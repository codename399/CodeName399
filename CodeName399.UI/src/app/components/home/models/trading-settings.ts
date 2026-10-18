export interface InstrumentTradingSettings {
  exchange: string;
  productType: string;
  orderType: string;
  duration: string;
  atrStopMultiplier: number;
  maximumSpreadPercent: number;
  minimumOrderBookLiquidityCoverageRatio: number;
  requireOrderBookLiquidityData: boolean;
  exitOrderTimeoutSeconds: number;
  maxMarketDataAgeSeconds: number;
  maximumExitRetries: number;
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
  autoSquareOff: boolean;
  paperTradingBalance: number;

  marketOpenTime: string;
  marketCloseTime: string;
  intradayEntryCutoffTime: string;
  equityMisAutoSquareOffTime: string;
  roboAutoSquareOffTime: string;

  watchListRefreshMinutes: number;
  lastDailySummarySent: string | null;
  instrumentLoadedAt: string | null;
  marketTimeZoneId: string;
  tradingHolidays: string[];

  enableEntryExecutionAudit: boolean;
  enableScripConsentForCashOrders: boolean;
  webSocketHeartbeatSeconds: number;
  webSocketPongTimeoutSeconds: number;
  webSocketRetryInitialSeconds: number;
  webSocketRetryMaxSeconds: number;
  brokerPositionConfirmationDelaySeconds: number;
  squareOffRetryDelaySeconds: number;

  buyTradingInterval: number;
  sellTradingInterval: number;
  visibleColumns: string[];

  rawMarketData: RawMarketDataSettings;
}
