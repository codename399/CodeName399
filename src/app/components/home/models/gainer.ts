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
    symbol: string;
    symbolToken: string;

    currentPrice: number;
    previousClose: number;
    changePercent: number;

    ema9: number;
    ema21: number;
    rsi: number;
    vwap: number;
    atr: number;

    volumeMultiplier: number;
    pullbackDistance: number;

    score: number;

    signal: string;
    risk: string;
    reason: string;

    stopLoss: number;
    targetPrice: number;

    isOwned: boolean;

    quantity: number;
    averagePrice: number;

    investedAmount: number;
    currentValue: number;

    profitLoss: number;
    profitLossPercentage: number;

    upperCircuitLimit: number;
    lowerCircuitLimit: number;

    suggestion: string;

    setupTime?: string | null;
    setupPrice: number;
    setupConfirmed: boolean;

    quote: MarketQuoteData;
}
