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

  // =========================
  // Trend
  // =========================

  emA9: number;
  emA21: number;
  emA50: number;
  emA200: number;
  atr: number;
  rsi: number;
  vwap: number;
  anchoredVWAP: number;

  // =========================
  // Advanced Indicators
  // =========================

  adx: number;
  plusDI: number;
  minusDI: number;
  superTrend: number;
  superTrendBullish: boolean;
  choppiness: number;
  relativeVolume: number;
  volumeMultiplier: number;
  emaSlope9: number;
  emaSlope21: number;

  // =========================
  // Price Action
  // =========================

  pullbackDistance: number;
  distanceFromEMA: number;
  distanceFromVWAP: number;
  distanceFromAnchoredVWAP: number;
  priceAboveEMA9: boolean;
  priceAboveVWAP: boolean;
  aboveAnchoredVWAP: boolean;
  ema9Bounce: boolean;
  vwapBounce: boolean;
  anchoredVWAPBounce: boolean;
  higherHigh: boolean;
  higherLow: boolean;
  lastCandleBullish: boolean;
  increasingMomentum: boolean;
  isExhaustedMove: boolean;

  // =========================
  // Evaluation
  // =========================

  score: number;
  signal: string;
  risk: string;
  reason: string;
  suggestion: string;

  // =========================
  // Trade Levels
  // =========================

  stopLoss: number;
  targetPrice: number;

  // =========================
  // Position
  // =========================

  isOwned: boolean;
  quantity: number;
  averagePrice: number;
  investedAmount: number;
  currentValue: number;
  profitLoss: number;
  profitLossPercentage: number;

  // =========================
  // Market
  // =========================

  upperCircuitLimit: number;
  lowerCircuitLimit: number;
  quote: MarketQuoteData;

  macd: number;
  macdSignal: number;
  macdHistogram: number;

  bollingerUpper: number;
  bollingerMiddle: number;
  bollingerLower: number;
  bollingerBandwidth: number;
  bollingerSqueeze: boolean;
  bollingerExpansion: boolean;

  setupTime?: string | null;
  setupPrice: number;
  setupConfirmed: boolean;
}
