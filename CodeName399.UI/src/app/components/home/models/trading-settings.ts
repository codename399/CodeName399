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
  enableNotification: boolean;
  chargeCoverLowestPriceChangeCandles: number;
  marketOpenTime: string;
  marketCloseTime: string;
  intradayCutoffTime: string;
  tradingHolidays: string[];
  excludedSymbols: string[];
  maxCandidates: number;
  candleBatchSize: number;
  batchDelay: number;
  buyBatchSize: number;
  autoSquareOffMinutes: number;
  candleInterval: string;
  lastDailySummarySent: string | null;
  instrumentLoadedAt: string | null;
  marketTimeZoneId: string;
  webSocketHeartbeatSeconds: number;
  webSocketPongTimeoutSeconds: number;
  webSocketRetryInitialSeconds: number;
  webSocketRetryMaxSeconds: number;
  visibleColumns: string[];
  autoSquareOff: boolean;
  paperTradingBalance: number;
  researchDataVersion: string;
  rawMarketData: RawMarketDataSettings;
}
