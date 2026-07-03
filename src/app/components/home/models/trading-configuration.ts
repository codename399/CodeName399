import { TradingStrategy } from './enum/trading-strategy';

export interface TradingConfiguration {
  id: string;

  enableAutoTrading: boolean;

  paperTrading: boolean;

  enableNotification: boolean;

  strategy: TradingStrategy;

  riskPercentage: number;

  maxCapitalPerTrade: number;

  maxDailyLoss: number;

  maxDailyTrades: number;

  cooldownMinutes: number;

  scanIntervalSeconds: number;

  ignoreMarketHours: boolean;

  enableTrailingStop: boolean;

  trailingStopPercentage: number;

  stopLossMultiplier: number;

  targetMultiplier: number;

  autoSquareOff: boolean;

  marketOpenTime: string;

  marketCloseTime: string;

  watchListRefreshMinutes: number;
}
