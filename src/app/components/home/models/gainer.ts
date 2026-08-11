export interface MarketQuoteData {
  exchange?: string;
  tradingSymbol?: string;
  symbolToken?: string;
  ltp?: number;
  productType?: string;
  instrumentType?: string;
  optionType?: string;
  strike?: number;
  expiry?: string;
  lotSize?: number;
  bid?: number;
  ask?: number;
  openInterest?: number;
  netChangeOpenInterest?: number;
  oiChangePercent?: number;
  tradeVolume?: number;
  delta?: number;
  gamma?: number;
  theta?: number;
  vega?: number;
  impliedVolatility?: number;
  underlyingSymbol?: string;
  underlyingPrice?: number;
  underlyingChangePercent?: number;
  underlyingBullish?: boolean;
  underlyingBearish?: boolean;
  callOIAtMax?: number;
  putOIAtMax?: number;
  callOIResistanceStrike?: number;
  putOISupportStrike?: number;
  callOIChange?: number;
  putOIChange?: number;
  pcr?: number;
  putCallRatio?: number;
  oiBuildup?: string;
  referenceLimitPrice?: number;
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
  quoteTimeUtc?: string;
  greeksUpdatedAtUtc?: string;
  [key: string]: unknown;
}

export interface Gainer {
  symbol: string;
  symbolToken: string;

  // API instrument / derivative identity
  instrumentType?: string;
  direction?: string;
  entryTransactionType?: string;
  isNakedOptionWrite?: boolean;
  exchange?: string;
  productType?: string;
  orderType?: string;
  duration?: string;
  underlyingSymbol?: string;
  expiry?: string;
  strike?: number;
  optionType?: string;
  lotSize?: number;
  underlyingPrice?: number;
  underlyingChangePercent?: number;
  underlyingBullish?: boolean;
  underlyingBearish?: boolean;

  // F&O analytics returned by the API
  openInterest?: number;
  netChangeOpenInterest?: number;
  oiChangePercent?: number;
  delta?: number;
  gamma?: number;
  theta?: number;
  vega?: number;
  impliedVolatility?: number;
  bid?: number;
  ask?: number;
  spread?: number;
  spreadPercent?: number;
  putCallRatio?: number;
  pcr?: number;
  oiBuildup?: string;
  callOIAtMax?: number;
  putOIAtMax?: number;
  callOIResistanceStrike?: number;
  putOISupportStrike?: number;
  oiChangeCall?: number;
  oiChangePut?: number;

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
  isSubscribed: boolean;

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
