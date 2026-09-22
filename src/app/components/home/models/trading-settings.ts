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
  minimumPrice: number;
  minimumVolume: number;
  atrStopMultiplier: number;
  maximumStopPercent: number;
  minimumStopPercent: number;
  allowLong: boolean;
  allowShort: boolean;
  riskPercentage: number;
  maxCapitalPerTradePercent: number;
  exitOrderTimeoutSeconds?: number;
  maxMarketDataAgeSeconds?: number;
  maximumExitRetries?: number;
  maximumSpreadPercent?: number;
  maximumSpreadAmount?: number;
  minimumBid?: number;
  minimumAsk?: number;
  maximumOpenPositions?: number;
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
  maxDailyLoss: number;
  maxDailyTrades: number;
  cooldownMinutes: number;
  ignoreMarketHours: boolean;
  marketOpenTime: string;
  marketCloseTime: string;
  autoSquareOff: boolean;
  paperTradingBalance: number;
  exit: {
    trailingStopAtrMultiplier: number;
    trailingProfitRetentionPercent: number;
  };
  [key: string]: any;
}
