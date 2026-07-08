export interface MarketQuoteData {
  symbol?: string;
  lastPrice?: number;
  previousClose?: number;
  change?: number;
  changePercent?: number;
  volume?: number;
  open?: number;
  high?: number;
  low?: number;
  vwap?: number;
  [key: string]: unknown;
}

export interface Gainer {
  // ---------- Instrument ----------

  symbol: string;
  symbolToken?: string;

  companyName?: string;
  exchange?: string;
  token?: string;

  // ---------- Price ----------

  currentPrice: number;
  previousClose: number;

  open?: number;
  high?: number;
  low?: number;
  volume?: number;

  // ---------- Performance ----------

  change?: number;
  changePercent: number;

  // ---------- Indicators ----------

  vwap: number;
  ema9?: number;
  emA9?: number;
  ema21?: number;
  emA21?: number;
  rsi?: number;

  volumeMultiplier: number;
  pullbackDistance: number;

  // ---------- Portfolio ----------

  isOwned: boolean;
  quantity?: number;
  averagePrice?: number;
  investedAmount?: number;
  currentValue?: number;
  profitLoss?: number;
  profitLossPercentage?: number;

  // ---------- Trading ----------

  signal: 'BUY' | 'SELL' | 'HOLD' | 'SETUP' | string;
  risk: 'LOW' | 'MEDIUM' | 'HIGH' | string;
  score: number;

  // ---------- Trade Levels ----------

  stopLoss: number;
  targetPrice: number;

  // ---------- Explanation ----------

  reason: string;

  // ---------- Additional Metrics ----------

  atr?: number;
  estimatedGrossProfit?: number;
  estimatedCharges?: number;
  estimatedNetProfit?: number;
  chargesPercent?: number;
  chargesAccepted?: boolean;
  chargesReason?: string;
  upperCircuitLimit?: number;
  lowerCircuitLimit?: number;

  // ---------- Metadata ----------

  quote?: MarketQuoteData;
  updatedAt?: string;
}
