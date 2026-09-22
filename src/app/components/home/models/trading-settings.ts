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

/**
 * Editable trading configuration exposed by the UI.
 * Runtime/API-only settings are intentionally not duplicated here; the save
 * operation preserves the complete configuration object returned by the API.
 * visibleColumns is retained because the grid uses it.
 */
export interface TradingConfiguration {
  id: string;
  enableAutoTrading: boolean;
  paperTrading: boolean;
  enableNotification: boolean;

  coreStrategy: CoreStrategySettings;
  equity: InstrumentTradingSettings;

  riskPercentage: number;
  maxCapitalPerTradePercent: number;
  maximumTotalOpenRisk: number;

  ignoreMarketHours: boolean;
  marketOpenTime: string;
  marketCloseTime: string;

  visibleColumns: string[];

  autoSquareOff: boolean;
  paperTradingBalance: number;

  exit: ExitSettings;
}
