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

  ignoreMarketHours: boolean;

  marketOpenTime: string; // "09:15:00"

  marketCloseTime: string; // "15:00:00"

  excludedSymbols: string[];

  watchListRefreshMinutes: number;

  minPrice: number;

  minVolume: number;

  maxCandidates: number;

  lastDailySummarySent: string | null; // e.g. "2026-07-09"

  maximumChargesPerTrade: number;

  buyTradingInterval: number;

  sellTradingInterval: number;

  visibleColumns?: string[];
}
