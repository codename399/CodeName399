export interface CoreStrategySettings {
  enabled: boolean;
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
  exitOrderTimeoutSeconds?: number;
  maxMarketDataAgeSeconds?: number;
  maximumExitRetries?: number;
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
  maximumTotalOpenRisk: number;
  ignoreMarketHours: boolean;
  marketOpenTime: string;
  marketCloseTime: string;
  autoSquareOff: boolean;
  paperTradingBalance: number;
  visibleColumns?: string[];
  exit: {
    trailingStopAtrMultiplier: number;
    trailingProfitRetentionPercent: number;
  };
}
