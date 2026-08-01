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

  minimumRoiPercent: number;

  minimumNetProfit: number;

  autoSquareOff: boolean;

  paperTradingBalance: number;

  enableEMA9: boolean;

  enableEMA21: boolean;

  enableEMA50: boolean;

  enableEMA200: boolean;

  enableATR: boolean;

  enableRSI: boolean;

  enableVWAP: boolean;

  enableADX: boolean;

  enableRelativeVolume: boolean;

  enableEMASlope: boolean;

  enableDistanceFromEMA: boolean;

  enableChoppiness: boolean;

  enableSuperTrend: boolean;

  enableAnchoredVWAP: boolean;

  enableMACD: boolean;

  enableBollinger: boolean;

  virtualTradeObservationSeconds: number;

  virtualTradeExpirySeconds: number;

  minimumVirtualProfitPercent: number;

  maximumVirtualPullbackPercent: number;

  buyTradingInterval: number;

  sellTradingInterval: number;

  visibleColumns?: string[];
}
