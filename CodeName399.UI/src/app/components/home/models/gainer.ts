export interface MarketQuoteData {
  exchange?: string;
  tradingSymbol?: string;
  symbolToken?: string;
  ltp?: number;
  productType?: string;
  bid?: number;
  ask?: number;
  previousClose?: number;
  volume?: number;
  open?: number;
  high?: number;
  low?: number;
  quoteTimeUtc?: string;
  [key: string]: unknown;
}

export interface Gainer {
  symbol: string;
  symbolToken: string;
  exchange?: string;
  productType?: string;
  orderType?: string;
  duration?: string;

  currentPrice: number;
  previousClose: number;
  changePercent: number;
  atr: number;
  recoveryScore: number;
  breakoutStrength: number;

  signal: string;
  risk: string;
  reason: string;
  suggestion: string;
  isSubscribed: boolean;

  stopLoss: number;

  isOwned: boolean;
  quantity: number;
  averagePrice: number;
  investedAmount: number;
  currentValue: number;
  profitLoss: number;
  profitLossPercentage: number;

  upperCircuitLimit: number;
  lowerCircuitLimit: number;
  quote: MarketQuoteData;
}
