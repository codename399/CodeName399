export interface InstrumentTradingSettings {
  exchange: string;
  productType: string;
  orderType: string;
  duration: string;
  /* Execution defaults used by explicit manual order endpoints. */
}

export interface RawMarketDataSettings {
  enabled: boolean;
  storagePath: string;
  maximumZipSizeMb: number;
  rollOverAtPercent: number;
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
  gainersPageSize: number;
  minimumBuyProfitChangePercent: number;
  enablePullbackOpportunityNotifications: boolean;
  enableNetProfitOpportunityNotifications: boolean;
  enableNotification: boolean;

  equity: InstrumentTradingSettings;

  maxCapitalPerTradePercent: number;
  ignoreMarketHours: boolean;
  paperTradingBalance: number;

  marketOpenTime: string;
  marketCloseTime: string;
  intradayEntryCutoffTime: string;

  watchListRefreshMinutes: number;
  lastDailySummarySent: string | null;
  instrumentLoadedAt: string | null;
  tradingHolidays: string[];

  researchDataVersion: string;
  enableScripConsentForCashOrders: boolean;
  webSocketHeartbeatSeconds: number;
  webSocketPongTimeoutSeconds: number;
  webSocketRetryInitialSeconds: number;
  webSocketRetryMaxSeconds: number;
  visibleColumns: string[];

  rawMarketData: RawMarketDataSettings;
}
