import { Component, OnDestroy, OnInit, inject } from '@angular/core';

import { CommonModule } from '@angular/common';

import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Router } from '@angular/router';
import { Subscription, catchError, finalize, of } from 'rxjs';
import { ToastService } from '../../../../../services/toast.service';
import {
  TradingConfiguration,
  InstrumentType,
  InstrumentTradingSettings,
  FuturesTradingSettings,
  OptionsTradingSettings,
  OptimizationSettings,
  OptimizationMode,
} from '../../../models/trading-configuration';
import { TradingOptimizationStatus } from '../../../models/trading-optimization-status';
import { TradingStrategy } from '../../../models/enum/trading-strategy';
import { AngelOneService } from '../../../services/angel-one.service';

@Component({
  selector: 'app-trading-settings',

  standalone: true,

  imports: [CommonModule, ReactiveFormsModule],

  templateUrl: './trading-settings.component.html',

  styleUrl: './trading-settings.component.css',
})
export class TradingSettingsComponent implements OnInit, OnDestroy {
  readonly #fb = inject(FormBuilder);

  readonly #angel = inject(AngelOneService);

  readonly #toastService = inject(ToastService);

  readonly #router = inject(Router);

  loading = false;

  saving = false;

  optimizationStatus: TradingOptimizationStatus | null = null;
  optimizationStatusLoading = false;
  #optimizationStatusSubscription?: Subscription;

  readonly strategies = [
    {
      value: 0,
      text: 'Momentum',
    },

    {
      value: 1,
      text: 'Pullback',
    },
  ];

  readonly instrumentTypes: { value: InstrumentType; text: string }[] = [
    { value: 'Equity', text: 'Equity' },
    { value: 'Futures', text: 'Futures' },
    { value: 'Options', text: 'Options' },
  ];

  /** Exchanges supported by each instrument profile. */
  readonly exchangeOptions: Record<InstrumentType, { value: string; text: string }[]> = {
    Equity: [
      { value: 'NSE', text: 'NSE' },
      { value: 'BSE', text: 'BSE' },
    ],
    Futures: [
      { value: 'NFO', text: 'NFO' },
      { value: 'BFO', text: 'BFO' },
      { value: 'MCX', text: 'MCX' },
      { value: 'CDS', text: 'CDS' },
    ],
    Options: [
      { value: 'NFO', text: 'NFO' },
      { value: 'BFO', text: 'BFO' },
      { value: 'MCX', text: 'MCX' },
      { value: 'CDS', text: 'CDS' },
    ],
  };

  get availableExchanges(): { value: string; text: string }[] {
    return this.exchangeOptions[this.selectedInstrumentType] ?? this.exchangeOptions.Equity;
  }

  private defaultExchange(type: InstrumentType): string {
    return type === 'Equity' ? 'NSE' : 'NFO';
  }

  private isExchangeAllowed(type: InstrumentType, exchange: unknown): boolean {
    const value = String(exchange ?? '').trim().toUpperCase();
    return this.exchangeOptions[type].some(option => option.value === value);
  }

  private normalizeExchange(type: InstrumentType, exchange: unknown): string {
    const value = String(exchange ?? '').trim().toUpperCase();
    return this.isExchangeAllowed(type, value) ? value : this.defaultExchange(type);
  }

  readonly optionSides = [
    { value: 'Both', text: 'CE + PE' },
    { value: 'Call', text: 'Call (CE)' },
    { value: 'Put', text: 'Put (PE)' },
  ];

  readonly optionTradeModes = [
    { value: 'Both', text: 'Long + Naked Short' },
    { value: 'LongOnly', text: 'Long Premium Only' },
    { value: 'ShortOnly', text: 'Naked Writing Only' },
  ];

  private profileDrafts: Partial<Record<InstrumentType, InstrumentTradingSettings>> = {};
  private activeInstrumentType: InstrumentType = 'Equity';

  get selectedInstrumentType(): InstrumentType {
    return (this.form?.controls?.instrumentType?.value as InstrumentType) ?? 'Equity';
  }

  get isEquity(): boolean {
    return this.selectedInstrumentType === 'Equity';
  }

  get isFutures(): boolean {
    return this.selectedInstrumentType === 'Futures';
  }

  get isOptions(): boolean {
    return this.selectedInstrumentType === 'Options';
  }

  get isMomentum(): boolean {
    return this.isEquity && Number(this.form?.controls?.strategy?.value) === TradingStrategy.Momentum;
  }

  strategyName(strategy: TradingStrategy | number | string | null | undefined): string {
    const value = Number(strategy);
    return value === TradingStrategy.Pullback ? 'Pullback' : 'Momentum';
  }

  get isPullback(): boolean {
    return this.isEquity && Number(this.form?.controls?.strategy?.value) === TradingStrategy.Pullback;
  }

  form = this.#fb.group({
    enableAutoTrading: [{ value: false, disabled: false }],

    paperTrading: [true],

    enableNotification: [true],

    strategy: [1, Validators.required],

    instrumentType: ['Equity' as InstrumentType, Validators.required],

    exchange: ['NSE', Validators.required],
    productType: ['INTRADAY', Validators.required],
    orderType: ['MARKET', Validators.required],
    duration: ['DAY', Validators.required],
    minimumPrice: [50, Validators.required],
    minimumVolume: [500000, Validators.required],
    atrStopMultiplier: [1.2, Validators.required],
    atrTargetMultiplier: [2.4, Validators.required],
    maximumStopPercent: [1.5, Validators.required],
    minimumStopPercent: [0.5, Validators.required],
    minimumRiskReward: [1.5, Validators.required],
    allowLong: [true],
    allowShort: [true],
    exitOrderTimeoutSeconds: [10, Validators.required],
    maxMarketDataAgeSeconds: [15, Validators.required],
    maximumExitRetries: [5, Validators.required],
    maximumSpreadPercent: [1.5, Validators.required],
    maximumSpreadAmount: [5, Validators.required],
    minimumBid: [0, Validators.required],
    minimumAsk: [0, Validators.required],
    greeksCacheSeconds: [15, Validators.required],
    maximumOpenPositions: [3, Validators.required],
    maximumRiskPerUnderlying: [2500, Validators.required],
    maximumPositionsPerUnderlying: [1, Validators.required],
    maximumLotsPerTrade: [0, Validators.required],
    maximumMarginUtilizationPercent: [70, Validators.required],
    forceSquareOffBufferMinutes: [15, Validators.required],

    futuresExpiryType: ['NEAR'],
    futuresMinimumOpenInterest: [0],
    futuresMinimumOIChangePercent: [0],
    futuresMaximumDailyLoss: [3000],
    futuresMaximumDailyTrades: [5],

    optionsOptionSide: ['Both'],
    optionsExpiryType: ['NEAR'],
    optionsStrikeStepsFromAtm: [1],
    optionsContractsPerUnderlying: [4],
    optionsMinimumOpenInterest: [0],
    optionsMinimumDelta: [0.25],
    optionsMaximumDelta: [0.8],
    optionsMaximumAbsoluteTheta: [1000],
    optionsMaximumImpliedVolatility: [100],
    optionsMinimumGamma: [0],
    optionsMinimumVega: [0],
    optionsStrikeInterval: [0],
    optionsMinimumOptionVolume: [0],
    optionsMinimumTurnover: [0],
    optionsMinimumPremium: [0],
    optionsMaximumPremium: [999999999],
    optionsMinimumOIChangePercent: [0],
    optionsAllowExpiryDayTrading: [true],
    optionsMinimumMinutesBeforeExpiry: [30],
    optionsMaximumExpiryDayIV: [100],
    optionsMaximumDailyLoss: [3000],
    optionsMaximumDailyTrades: [5],
    optionsExpiryMarketCloseTime: ['15:40'],
    optionsRequireMarketDepth: [true],
    optionsMaximumBidAskSpreadPercent: [1],
    optionsMaximumBidAskSpreadAmount: [5],
    optionsMinimumOptionTurnover: [0],
    optionsRequireFreshGreeks: [true],
    optionsGreeksFreshnessSeconds: [15],
    optionsMaximumStrikeCandidatesPerSide: [3],
    optionsMinimumCallScore: [75],
    optionsMinimumPutScore: [75],
    optionsMinimumPCR: [0],
    optionsMaximumPCR: [999999999],
    optionsUseUnderlyingMultiTimeframeTrend: [true],
    optionsTradeMode: ['Both'],
    optionsAllowNakedWriting: [true],
    optionsAllowNakedCallWriting: [true],
    optionsAllowNakedPutWriting: [true],
    optionsAllowNakedWritingOnExpiryDay: [false],
    optionsShortMinimumDelta: [0.1],
    optionsShortMaximumDelta: [0.6],
    optionsShortMinimumIV: [0],
    optionsShortMaximumIV: [100],
    optionsShortMinimumThetaAbs: [0],
    optionsShortMaximumThetaAbs: [1000],
    optionsShortMinimumPremium: [0],
    optionsShortMaximumPremium: [999999999],
    optionsMinimumShortCallScore: [75],
    optionsMinimumShortPutScore: [75],
    optionsMaximumNakedOptionRiskPerTrade: [5000],
    optionsMaximumNakedOptionLotsPerTrade: [2],
    optionsNakedOptionMarginSafetyMultiplier: [1.2],
    optionsMaximumUnderlyingDeltaExposure: [750],
    optionsMaximumExpiryDayRiskMultiplier: [0.5],
    optionsNakedStressUnderlyingMovePercent: [3],
    optionsNakedStressIVIncreasePercent: [10],
    optionsMaximumNakedStressLossPerTrade: [5000],
    optionsMaximumUnderlyingStressLoss: [7500],
    optionsMaximumOpenDeltaExposure: [1000],
    optionsMaximumOpenGammaExposure: [100],
    optionsMaximumOpenVegaExposure: [1000],
    optionsMaximumShortLotsPerExpiry: [5],
    optionsMaximumShortLotsPerUnderlying: [8],
    optionsMaximumShortLotsPerStrike: [2],
    optionsMaximumShortPremiumExposure: [100000],
    optionsAllowNakedStrangle: [false],
    optionsAllowNakedStraddle: [false],
    optionsEmergencyDeltaExposure: [1000],
    optionsEmergencyGammaExposure: [100],
    optionsEmergencyVegaExposure: [1000],
    optionsEmergencyIVIncreasePercent: [25],
    optionsEmergencyStressLoss: [7500],
    optionsEmergencyMarginUtilizationPercent: [85],
    optionsNakedRiskMonitorSeconds: [5],
    optionsMaximumRiskPerTrade: [0],

    riskPercentage: [
      2,

      [Validators.required, Validators.min(0.1), Validators.max(100)],
    ],

    maxCapitalPerTrade: [10000, Validators.required],

    maxDailyLoss: [3000, Validators.required],

    maxDailyTrades: [5, Validators.required],

    cooldownMinutes: [10, Validators.required],

    ignoreMarketHours: [false],

    marketOpenTime: ['09:15', Validators.required],

    marketCloseTime: ['15:00', Validators.required],

    excludedSymbolsText: [''],

    watchListRefreshMinutes: [2, Validators.required],

    minPrice: [50, Validators.required],

    minVolume: [500000, Validators.required],

    maxCandidates: [100, Validators.required],

    maximumChargesPerTrade: [100, Validators.required],

    minimumRoiPercent: [
      0.3,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],

    minimumNetProfit: [5, Validators.required],

    autoSquareOff: [true],

    paperTradingBalance: [100000, Validators.required],

    virtualTradeObservationSeconds: [20, Validators.required],

    virtualTradeExpirySeconds: [60, Validators.required],

    minimumVirtualProfitPercent: [
      0.25,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],

    maximumVirtualPullbackPercent: [
      0.5,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],

    buyTradingInterval: [5, Validators.required],

    sellTradingInterval: [1000, Validators.required],

    enableEMA9: [true],

    enableEMA21: [true],

    enableEMA50: [true],

    enableEMA200: [true],

    enableATR: [true],

    enableRSI: [true],

    enableVWAP: [true],

    enableADX: [true],

    enableRelativeVolume: [true],

    enableEMASlope: [true],

    enableDistanceFromEMA: [true],

    enableChoppiness: [true],

    enableSuperTrend: [true],

    enableAnchoredVWAP: [true],

    enableMACD: [true],

    enableBollinger: [true],

    validation: this.#fb.group({
      minimumMovementScore: [
        45,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumConfidence: [
        65,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumRiskReward: [1.5, [Validators.required, Validators.min(0)]],
      minimumTrendStrength: [
        50,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumTrendStability: [
        55,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumRecoveryScore: [
        50,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumVolatilityScore: [
        30,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumNoiseScore: [
        45,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumBreakoutStrength: [
        45,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumRSI: [
        50,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      maximumRSI: [
        72,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumVolumeMultiplier: [1.5, [Validators.required, Validators.min(0)]],
      minimumPositiveTickRatio: [
        0.55,
        [Validators.required, Validators.min(0), Validators.max(1)],
      ],
      minimumAboveEntryRatio: [
        0.55,
        [Validators.required, Validators.min(0), Validators.max(1)],
      ],
      minimumHigherHighs: [2, [Validators.required, Validators.min(0)]],
      minimumConsecutivePositiveTicks: [
        2,
        [Validators.required, Validators.min(0)],
      ],
      maximumConsecutiveNegativeTicks: [
        6,
        [Validators.required, Validators.min(0)],
      ],
      maximumDrawdownPercent: [
        0.4,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumRejectMovementScore: [
        30,
        [Validators.min(0), Validators.max(100)],
      ],
      maximumRejectDrawdownPercent: [
        0.6,
        [Validators.min(0), Validators.max(100)],
      ],
      minimumRejectPositiveTickRatio: [
        0.35,
        [Validators.min(0), Validators.max(1)],
      ],
      minimumRejectAboveEntryRatio: [
        0.25,
        [Validators.min(0), Validators.max(1)],
      ],
      maximumRejectConsecutiveNegativeTicks: [6, [Validators.min(0)]],
      minimumRejectProfitPercent: [0.1, [Validators.min(0)]],
      minimumGainPercent: [0.12, [Validators.min(0)]],
      minimumPriceSlope: [0.1, [Validators.min(0)]],
      momentumMinimumPriceRatio: [
        0.998,
        [Validators.min(0), Validators.max(1)],
      ],
      momentumMaximumDrawdown: [0.5, [Validators.min(0), Validators.max(1)]],
      momentumHighestPriceTolerance: [
        0.997,
        [Validators.min(0), Validators.max(1)],
      ],
      pullbackMinimumPriceRatio: [
        0.998,
        [Validators.min(0), Validators.max(1)],
      ],
      maximumPullbackGain: [0.75, [Validators.min(0), Validators.max(1)]],
      minimumFinalScore: [70, [Validators.min(0), Validators.max(100)]],
      minimumBollingerBandwidth: [1, [Validators.min(0)]],
      minimumFinalRSI: [48, [Validators.min(0), Validators.max(100)]],
      maximumFinalRSI: [72, [Validators.min(0), Validators.max(100)]],
    }),

    evaluation: this.#fb.group({
      strongAdx: [
        30,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      mediumAdx: [
        25,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      lowChoppiness: [
        38,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      highChoppiness: [
        55,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      highRelativeVolume: [2, [Validators.required, Validators.min(0)]],
      mediumRelativeVolume: [1.5, [Validators.required, Validators.min(0)]],
      lowRelativeVolume: [1.2, [Validators.required, Validators.min(0)]],
      excellentScore: [
        90,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      goodScore: [
        80,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      averageScore: [
        70,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],

      ema9AboveEma21Score: [10],
      ema21AboveEma50Score: [5],
      superTrendBullishScore: [5],
      priceAboveVwapScore: [5],
      anchoredVwapScore: [5],

      strongAdxScore: [10],
      mediumAdxScore: [6],
      plusDiAboveMinusDiScore: [5],
      lowChoppinessScore: [5],

      momentumIncreasingScore: [8],
      pullbackIncreasingMomentumScore: [8],
      lastCandleBullishScore: [5],
      higherHighScore: [3],
      higherLowScore: [3],
      ema9SlopePositiveScore: [2],
      ema21SlopePositiveScore: [2],

      highRelativeVolumeScore: [15],
      mediumRelativeVolumeScore: [10],
      lowRelativeVolumeScore: [5],

      pullbackHighRelativeVolumeScore: [10],
      pullbackMediumRelativeVolumeScore: [7],
      pullbackLowRelativeVolumeScore: [4],

      momentumPullbackIdealMinimum: [0],
      momentumPullbackIdealMaximum: [1.5],
      momentumPullbackMaximum: [2.5],
      momentumPullbackIdealScore: [10],
      momentumPullbackSecondaryScore: [5],

      pullbackDistanceMinimum: [0.3],
      pullbackDistanceIdealMaximum: [1.2],
      pullbackDistanceMaximum: [2],
      pullbackDistanceIdealScore: [10],
      pullbackDistanceSecondaryScore: [5],
      ema9BounceScore: [5],
      vwapBounceScore: [5],

      rsiValidScore: [10],
      rsiAboveMinimumScore: [5],
      rsiBelowMinimumPenalty: [-15],
      momentumRsiBelowMinimumPenalty: [-10],

      macdBullishScore: [6],
      macdBearishPenalty: [-8],
      macdHistogramPositiveScore: [4],
      macdHistogramNegativePenalty: [-4],

      momentumWideBollingerBandwidth: [3],
      momentumMediumBollingerBandwidth: [2],
      momentumNarrowBollingerBandwidth: [1],
      momentumWideBollingerScore: [5],
      momentumMediumBollingerScore: [3],
      momentumNarrowBollingerPenalty: [-8],

      pullbackWideBollingerBandwidth: [2],
      pullbackNarrowBollingerBandwidth: [1],
      pullbackWideBollingerScore: [5],
      pullbackNarrowBollingerPenalty: [-5],

      momentumExhaustedMovePenalty: [-15],
      pullbackExhaustedMovePenalty: [-20],
      superTrendBearishPenalty: [-10],
      highChoppinessPenalty: [-10],
    }),

    virtualTrading: this.#fb.group({
      warmupSeconds: [8, [Validators.required, Validators.min(0)]],
      observationSeconds: [15, [Validators.required, Validators.min(0)]],
      maximumObservationSeconds: [45, [Validators.required, Validators.min(0)]],
      tickWindow: [20, [Validators.required, Validators.min(0)]],
      entryLossPercent: [
        0.2,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      highestPriceTolerance: [
        0.3,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      tradeExpirySeconds: [30, [Validators.min(0)]],
      minimumObservationForTrendSeconds: [30, [Validators.min(0)]],
      confidenceBonusAfterSeconds1: [20, [Validators.min(0)]],
      confidenceBonusAfterSeconds2: [35, [Validators.min(0)]],
      pullbackWarmupSeconds: [8, [Validators.min(0)]],
    }),

    confidence: this.#fb.group({
      maximumGainBonus: [15, [Validators.min(0)]],
      gainBonusMultiplier: [20, [Validators.min(0)]],
      strongPositiveTickRatio: [3, [Validators.min(0)]],
      mediumPositiveTickRatio: [2, [Validators.min(0)]],
      strongTickBonus: [5, [Validators.min(0)]],
      mediumTickBonus: [3, [Validators.min(0)]],
      weakTickBonus: [1, [Validators.min(0)]],
      maximumPositiveStreakBonus: [5, [Validators.min(0)]],
      maximumHigherHighBonus: [5, [Validators.min(0)]],
      drawdownPenaltyMultiplier: [5, [Validators.min(0)]],
      idealMinimumRSI: [55, [Validators.min(0), Validators.max(100)]],
      idealMaximumRSI: [65, [Validators.min(0), Validators.max(100)]],
      overboughtRSI: [70, [Validators.min(0), Validators.max(100)]],
      oversoldRSI: [50, [Validators.min(0), Validators.max(100)]],
      macdBullishBonus: [4, [Validators.min(0)]],
      macdBearishPenalty: [-6, [Validators.min(-100), Validators.max(100)]],
      macdHistogramBonus: [2, [Validators.min(0)]],
      macdHistogramPenalty: [-2, [Validators.min(-100), Validators.max(100)]],
      highVolumeMultiplier: [3, [Validators.min(0)]],
      mediumVolumeMultiplier: [2, [Validators.min(0)]],
      lowVolumeMultiplier: [1.5, [Validators.min(0)]],
      highVolumeBonus: [3, [Validators.min(0)]],
      mediumVolumeBonus: [2, [Validators.min(0)]],
      lowVolumeBonus: [1, [Validators.min(0)]],
      wideBandwidth: [3, [Validators.min(0)]],
      narrowBandwidth: [1, [Validators.min(0)]],
      wideBandwidthBonus: [3, [Validators.min(0)]],
      narrowBandwidthPenalty: [-4, [Validators.min(-100), Validators.max(100)]],
      observationBonus1: [2, [Validators.min(0)]],
      observationBonus2: [2, [Validators.min(0)]],
      movementWeight: [0.15, [Validators.min(0), Validators.max(1)]],
      trendStrengthWeight: [0.05, [Validators.min(0), Validators.max(1)]],
      trendStabilityWeight: [0.05, [Validators.min(0), Validators.max(1)]],
      recoveryWeight: [0.03, [Validators.min(0), Validators.max(1)]],
      breakoutWeight: [0.02, [Validators.min(0), Validators.max(1)]],
    }),

    reporting: this.#fb.group({
      strongBuyConfidence: [95, [Validators.min(0), Validators.max(100)]],
      buyConfidence: [90, [Validators.min(0), Validators.max(100)]],
      watchConfidence: [85, [Validators.min(0), Validators.max(100)]],
      movementScoreThreshold: [90, [Validators.min(0), Validators.max(100)]],
      trendStrengthThreshold: [80, [Validators.min(0), Validators.max(100)]],
      trendStabilityThreshold: [80, [Validators.min(0), Validators.max(100)]],
      recoveryScoreThreshold: [80, [Validators.min(0), Validators.max(100)]],
      higherHighCountThreshold: [5, [Validators.min(0)]],
      volumeMultiplierThreshold: [2, [Validators.min(0)]],
      maxDrawdownPercentThreshold: [
        0.25,
        [Validators.min(0), Validators.max(1)],
      ],
      volatilityScoreThreshold: [60, [Validators.min(0), Validators.max(100)]],
      riskRewardThreshold: [2, [Validators.min(0)]],
    }),

    optimization: this.#fb.group({
      enabled: [true],
      paperTradingOnly: [true],
      mode: [0 as OptimizationMode],
      timeBasedCandidateMinutes: [
        60,
        [Validators.required, Validators.min(1)],
      ],
      sendConfigurationEmail: [true],
      sendDailyEmail: [true],
      autoPromoteBestConfiguration: [true],

      pollIntervalSeconds: [5, [Validators.required, Validators.min(1)]],
      minimumCandidateMinutes: [5, [Validators.required, Validators.min(0)]],
      noSignalTimeoutMinutes: [20, [Validators.required, Validators.min(0)]],
      noVirtualConfirmationTimeoutMinutes: [12, [Validators.required, Validators.min(0)]],
      noPaperTradeTimeoutMinutes: [10, [Validators.required, Validators.min(0)]],
      maxVirtualTradeDurationMinutes: [5, [Validators.required, Validators.min(0)]],
      maxPaperTradeDurationMinutes: [20, [Validators.required, Validators.min(0)]],
      forceCloseTimedOutPaperTrades: [true],
      maximumCandidateMinutes: [45, [Validators.required, Validators.min(0)]],

      maximumCandidatesPerDay: [20, [Validators.required, Validators.min(1)]],
      minimumCompletedTradesForAcceptance: [10, [Validators.required, Validators.min(0)]],
      preferredCompletedTrades: [20, [Validators.required, Validators.min(0)]],
      minimumVirtualCandidatesForAnalysis: [5, [Validators.required, Validators.min(0)]],

      minimumNetProfit: [0, [Validators.required]],
      minimumProfitFactor: [1.2, [Validators.required, Validators.min(0)]],
      maximumDrawdownPercent: [10, [Validators.required, Validators.min(0)]],

      dailyEmailDelayMinutes: [5, [Validators.required, Validators.min(0)]],
      validationHistoryFile: [
        'OptimizationRuns/validation-configuration-history.json',
        [Validators.required],
      ],
      reportDirectory: ['OptimizationRuns', [Validators.required]],
    }),

    exit: this.#fb.group({
      atrExitMultiplier: [
        0.4,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      minimumProfitPercent: [
        0.25,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      trailingActivationNetProfit: [
        0,
        [Validators.required, Validators.min(0)],
      ],
      trailingStopAtrMultiplier: [
        0.6,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
    }),
  });

  enableAutoTradingFormControl = this.form?.controls?.enableAutoTrading;
  enableAutoTradingPreviousValue = this.enableAutoTradingFormControl?.value;

  /** Keep the configuration page to one open accordion at a time. */
  onAccordionToggle(event: Event): void {
    const current = event.currentTarget as HTMLDetailsElement | null;

    if (!current?.open) {
      return;
    }

    const container = current.closest('.accordion-grid');
    if (!container) {
      return;
    }

    container
      .querySelectorAll<HTMLDetailsElement>('details.settings-card[open]')
      .forEach((accordion) => {
        if (accordion !== current) {
          accordion.open = false;
        }
      });
  }

  // ======================================================
  // Lifecycle
  // ======================================================

  ngOnInit(): void {
    this.enableAutoTradingFormControl?.valueChanges?.subscribe((value) => {
      if (value == this.enableAutoTradingPreviousValue) {
        return;
      }

      const confirmed = window.confirm(
        'Are you sure, you want to toggle auto trading?',
      );

      if (confirmed) {
        this.enableAutoTradingPreviousValue = value;
      } else {
        this.enableAutoTradingFormControl?.setValue(
          this.enableAutoTradingPreviousValue,
          { emitEvent: false },
        );
      }
    });

    this.form.controls.instrumentType.valueChanges.subscribe((value) => {
      if (value) {
        this.onInstrumentTypeChanged(value as InstrumentType);
      }
    });

    this.form.controls.strategy.valueChanges.subscribe(() => {
      // Strategy visibility is derived from the current form value. Mark the form dirty
      // because changing strategy changes which strategy-specific settings are active.
      this.form.markAsDirty();
    });

    this.loadConfiguration();

    // Load optimizer status once when this page is entered.
    // Do not poll every few seconds: the optimizer is a backend process and
    // page refreshes should not create a continuous HTTP polling loop.
    this.refreshOptimizationStatus();
  }

  refreshOptimizationStatus(): void {
    if (this.optimizationStatusLoading) {
      return;
    }

    this.optimizationStatusLoading = true;

    this.#optimizationStatusSubscription?.unsubscribe();
    this.#optimizationStatusSubscription = this.#angel
      .getTradingOptimizationStatus()
      .pipe(
        catchError(() => of(null)),
        finalize(() => {
          this.optimizationStatusLoading = false;
        }),
      )
      .subscribe((status) => {
        if (status) {
          this.optimizationStatus = status;
        }
      });
  }

  ngOnDestroy(): void {
    this.#optimizationStatusSubscription?.unsubscribe();
  }

  // ======================================================
  // Load Configuration
  // ======================================================

  private loadConfiguration(): void {
    this.loading = true;

    this.#angel

      .getTradingConfiguration()

      .pipe(
        finalize(() => {
          this.loading = false;
        }),
      )

      .subscribe({
        next: (configuration) => {
          this.patchForm(configuration);
        },

        error: () => {
          this.#toastService.error('Unable to load trading configuration');
        },
      });
  }

  // ======================================================
  // Patch Form
  // ======================================================

  private patchForm(configuration: TradingConfiguration): void {
    this.form.patchValue(
      {
        instrumentType: configuration.instrumentType ?? 'Equity',

        enableAutoTrading: configuration.enableAutoTrading,

        paperTrading: configuration.paperTrading,

        enableNotification: configuration.enableNotification,

        strategy: this.normalizeStrategy(configuration.strategy),

        riskPercentage: configuration.riskPercentage,

        maxCapitalPerTrade: configuration.maxCapitalPerTrade,

        maxDailyLoss: configuration.maxDailyLoss,

        maxDailyTrades: configuration.maxDailyTrades,

        cooldownMinutes: configuration.cooldownMinutes,

        ignoreMarketHours: configuration.ignoreMarketHours,

        marketOpenTime: this.toTimeInput(configuration.marketOpenTime),

        marketCloseTime: this.toTimeInput(configuration.marketCloseTime),

        excludedSymbolsText: (configuration.excludedSymbols ?? []).join(', '),

        watchListRefreshMinutes: configuration.watchListRefreshMinutes,

        minPrice: configuration.minPrice,

        minVolume: configuration.minVolume,

        maxCandidates: configuration.maxCandidates,

        minimumRoiPercent: configuration.minimumRoiPercent,

        minimumNetProfit: configuration.minimumNetProfit,

        autoSquareOff: configuration.autoSquareOff,

        paperTradingBalance: configuration.paperTradingBalance,

        virtualTradeObservationSeconds:
          configuration.virtualTradeObservationSeconds,

        virtualTradeExpirySeconds: configuration.virtualTradeExpirySeconds,

        minimumVirtualProfitPercent: configuration.minimumVirtualProfitPercent,

        maximumVirtualPullbackPercent:
          configuration.maximumVirtualPullbackPercent,

        buyTradingInterval: configuration.buyTradingInterval,

        sellTradingInterval: configuration.sellTradingInterval,

        enableEMA9: configuration.enableEMA9,

        enableEMA21: configuration.enableEMA21,

        enableEMA50: configuration.enableEMA50,

        enableEMA200: configuration.enableEMA200,

        enableATR: configuration.enableATR,

        enableRSI: configuration.enableRSI,

        enableVWAP: configuration.enableVWAP,

        enableADX: configuration.enableADX,

        enableRelativeVolume: configuration.enableRelativeVolume,

        enableEMASlope: configuration.enableEMASlope,

        enableDistanceFromEMA: configuration.enableDistanceFromEMA,

        enableChoppiness: configuration.enableChoppiness,

        enableSuperTrend: configuration.enableSuperTrend,

        enableAnchoredVWAP: configuration.enableAnchoredVWAP,

        enableMACD: configuration.enableMACD,

        enableBollinger: configuration.enableBollinger,

        validation: {
          minimumMovementScore:
            configuration.validation?.minimumMovementScore ?? 45,
          minimumConfidence: configuration.validation?.minimumConfidence ?? 65,
          minimumRiskReward: configuration.validation?.minimumRiskReward ?? 1.5,
          minimumTrendStrength:
            configuration.validation?.minimumTrendStrength ?? 50,
          minimumTrendStability:
            configuration.validation?.minimumTrendStability ?? 55,
          minimumRecoveryScore:
            configuration.validation?.minimumRecoveryScore ?? 50,
          minimumVolatilityScore:
            configuration.validation?.minimumVolatilityScore ?? 30,
          minimumNoiseScore: configuration.validation?.minimumNoiseScore ?? 45,
          minimumBreakoutStrength:
            configuration.validation?.minimumBreakoutStrength ?? 45,
          minimumRSI: configuration.validation?.minimumRSI ?? 50,
          maximumRSI: configuration.validation?.maximumRSI ?? 72,
          minimumVolumeMultiplier:
            configuration.validation?.minimumVolumeMultiplier ?? 1.5,
          minimumPositiveTickRatio:
            configuration.validation?.minimumPositiveTickRatio ?? 0.55,
          minimumAboveEntryRatio:
            configuration.validation?.minimumAboveEntryRatio ?? 0.55,
          minimumHigherHighs: configuration.validation?.minimumHigherHighs ?? 2,
          minimumConsecutivePositiveTicks:
            configuration.validation?.minimumConsecutivePositiveTicks ?? 2,
          maximumConsecutiveNegativeTicks:
            configuration.validation?.maximumConsecutiveNegativeTicks ?? 6,
          maximumDrawdownPercent:
            configuration.validation?.maximumDrawdownPercent ?? 0.4,
          minimumRejectMovementScore:
            configuration.validation?.minimumRejectMovementScore ?? 30,
          maximumRejectDrawdownPercent:
            configuration.validation?.maximumRejectDrawdownPercent ?? 0.6,
          minimumRejectPositiveTickRatio:
            configuration.validation?.minimumRejectPositiveTickRatio ?? 0.35,
          minimumRejectAboveEntryRatio:
            configuration.validation?.minimumRejectAboveEntryRatio ?? 0.25,
          maximumRejectConsecutiveNegativeTicks:
            configuration.validation?.maximumRejectConsecutiveNegativeTicks ??
            6,
          minimumRejectProfitPercent:
            configuration.validation?.minimumRejectProfitPercent ?? 0.1,
          minimumGainPercent:
            configuration.validation?.minimumGainPercent ?? 0.12,
          minimumPriceSlope: configuration.validation?.minimumPriceSlope ?? 0.1,
          momentumMinimumPriceRatio:
            configuration.validation?.momentumMinimumPriceRatio ?? 0.998,
          momentumMaximumDrawdown:
            configuration.validation?.momentumMaximumDrawdown ?? 0.5,
          momentumHighestPriceTolerance:
            configuration.validation?.momentumHighestPriceTolerance ?? 0.997,
          pullbackMinimumPriceRatio:
            configuration.validation?.pullbackMinimumPriceRatio ?? 0.998,
          maximumPullbackGain:
            configuration.validation?.maximumPullbackGain ?? 0.75,
          minimumFinalScore: configuration.validation?.minimumFinalScore ?? 70,
          minimumBollingerBandwidth:
            configuration.validation?.minimumBollingerBandwidth ?? 1,
          minimumFinalRSI: configuration.validation?.minimumFinalRSI ?? 48,
          maximumFinalRSI: configuration.validation?.maximumFinalRSI ?? 72,
        },

        evaluation: {
          strongAdx: configuration.evaluation?.strongAdx ?? 30,
          mediumAdx: configuration.evaluation?.mediumAdx ?? 25,
          lowChoppiness: configuration.evaluation?.lowChoppiness ?? 38,
          highChoppiness: configuration.evaluation?.highChoppiness ?? 55,
          highRelativeVolume: configuration.evaluation?.highRelativeVolume ?? 2,
          mediumRelativeVolume:
            configuration.evaluation?.mediumRelativeVolume ?? 1.5,
          lowRelativeVolume: configuration.evaluation?.lowRelativeVolume ?? 1.2,
          excellentScore: configuration.evaluation?.excellentScore ?? 90,
          goodScore: configuration.evaluation?.goodScore ?? 80,
          averageScore: configuration.evaluation?.averageScore ?? 70,

          ema9AboveEma21Score:
            configuration.evaluation?.ema9AboveEma21Score ?? 10,
          ema21AboveEma50Score:
            configuration.evaluation?.ema21AboveEma50Score ?? 5,
          superTrendBullishScore:
            configuration.evaluation?.superTrendBullishScore ?? 5,
          priceAboveVwapScore:
            configuration.evaluation?.priceAboveVwapScore ?? 5,
          anchoredVwapScore: configuration.evaluation?.anchoredVwapScore ?? 5,

          strongAdxScore: configuration.evaluation?.strongAdxScore ?? 10,
          mediumAdxScore: configuration.evaluation?.mediumAdxScore ?? 6,
          plusDiAboveMinusDiScore:
            configuration.evaluation?.plusDiAboveMinusDiScore ?? 5,
          lowChoppinessScore: configuration.evaluation?.lowChoppinessScore ?? 5,

          momentumIncreasingScore:
            configuration.evaluation?.momentumIncreasingScore ?? 8,
          pullbackIncreasingMomentumScore:
            configuration.evaluation?.pullbackIncreasingMomentumScore ?? 8,
          lastCandleBullishScore:
            configuration.evaluation?.lastCandleBullishScore ?? 5,
          higherHighScore: configuration.evaluation?.higherHighScore ?? 3,
          higherLowScore: configuration.evaluation?.higherLowScore ?? 3,
          ema9SlopePositiveScore:
            configuration.evaluation?.ema9SlopePositiveScore ?? 2,
          ema21SlopePositiveScore:
            configuration.evaluation?.ema21SlopePositiveScore ?? 2,

          highRelativeVolumeScore:
            configuration.evaluation?.highRelativeVolumeScore ?? 15,
          mediumRelativeVolumeScore:
            configuration.evaluation?.mediumRelativeVolumeScore ?? 10,
          lowRelativeVolumeScore:
            configuration.evaluation?.lowRelativeVolumeScore ?? 5,

          pullbackHighRelativeVolumeScore:
            configuration.evaluation?.pullbackHighRelativeVolumeScore ?? 10,
          pullbackMediumRelativeVolumeScore:
            configuration.evaluation?.pullbackMediumRelativeVolumeScore ?? 7,
          pullbackLowRelativeVolumeScore:
            configuration.evaluation?.pullbackLowRelativeVolumeScore ?? 4,

          momentumPullbackIdealMinimum:
            configuration.evaluation?.momentumPullbackIdealMinimum ?? 0,
          momentumPullbackIdealMaximum:
            configuration.evaluation?.momentumPullbackIdealMaximum ?? 1.5,
          momentumPullbackMaximum:
            configuration.evaluation?.momentumPullbackMaximum ?? 2.5,
          momentumPullbackIdealScore:
            configuration.evaluation?.momentumPullbackIdealScore ?? 10,
          momentumPullbackSecondaryScore:
            configuration.evaluation?.momentumPullbackSecondaryScore ?? 5,

          pullbackDistanceMinimum:
            configuration.evaluation?.pullbackDistanceMinimum ?? 0.3,
          pullbackDistanceIdealMaximum:
            configuration.evaluation?.pullbackDistanceIdealMaximum ?? 1.2,
          pullbackDistanceMaximum:
            configuration.evaluation?.pullbackDistanceMaximum ?? 2,
          pullbackDistanceIdealScore:
            configuration.evaluation?.pullbackDistanceIdealScore ?? 10,
          pullbackDistanceSecondaryScore:
            configuration.evaluation?.pullbackDistanceSecondaryScore ?? 5,
          ema9BounceScore: configuration.evaluation?.ema9BounceScore ?? 5,
          vwapBounceScore: configuration.evaluation?.vwapBounceScore ?? 5,

          rsiValidScore: configuration.evaluation?.rsiValidScore ?? 10,
          rsiAboveMinimumScore:
            configuration.evaluation?.rsiAboveMinimumScore ?? 5,
          rsiBelowMinimumPenalty:
            configuration.evaluation?.rsiBelowMinimumPenalty ?? -15,
          momentumRsiBelowMinimumPenalty:
            configuration.evaluation?.momentumRsiBelowMinimumPenalty ?? -10,

          macdBullishScore: configuration.evaluation?.macdBullishScore ?? 6,
          macdBearishPenalty:
            configuration.evaluation?.macdBearishPenalty ?? -8,
          macdHistogramPositiveScore:
            configuration.evaluation?.macdHistogramPositiveScore ?? 4,
          macdHistogramNegativePenalty:
            configuration.evaluation?.macdHistogramNegativePenalty ?? -4,

          momentumWideBollingerBandwidth:
            configuration.evaluation?.momentumWideBollingerBandwidth ?? 3,
          momentumMediumBollingerBandwidth:
            configuration.evaluation?.momentumMediumBollingerBandwidth ?? 2,
          momentumNarrowBollingerBandwidth:
            configuration.evaluation?.momentumNarrowBollingerBandwidth ?? 1,
          momentumWideBollingerScore:
            configuration.evaluation?.momentumWideBollingerScore ?? 5,
          momentumMediumBollingerScore:
            configuration.evaluation?.momentumMediumBollingerScore ?? 3,
          momentumNarrowBollingerPenalty:
            configuration.evaluation?.momentumNarrowBollingerPenalty ?? -8,

          pullbackWideBollingerBandwidth:
            configuration.evaluation?.pullbackWideBollingerBandwidth ?? 2,
          pullbackNarrowBollingerBandwidth:
            configuration.evaluation?.pullbackNarrowBollingerBandwidth ?? 1,
          pullbackWideBollingerScore:
            configuration.evaluation?.pullbackWideBollingerScore ?? 5,
          pullbackNarrowBollingerPenalty:
            configuration.evaluation?.pullbackNarrowBollingerPenalty ?? -5,

          momentumExhaustedMovePenalty:
            configuration.evaluation?.momentumExhaustedMovePenalty ?? -15,
          pullbackExhaustedMovePenalty:
            configuration.evaluation?.pullbackExhaustedMovePenalty ?? -20,
          superTrendBearishPenalty:
            configuration.evaluation?.superTrendBearishPenalty ?? -10,
          highChoppinessPenalty:
            configuration.evaluation?.highChoppinessPenalty ?? -10,
        },

        virtualTrading: {
          warmupSeconds: configuration.virtualTrading?.warmupSeconds ?? 8,
          observationSeconds:
            configuration.virtualTrading?.observationSeconds ?? 15,
          maximumObservationSeconds:
            configuration.virtualTrading?.maximumObservationSeconds ?? 45,
          tickWindow: configuration.virtualTrading?.tickWindow ?? 20,
          entryLossPercent:
            configuration.virtualTrading?.entryLossPercent ?? 0.2,
          highestPriceTolerance:
            configuration.virtualTrading?.highestPriceTolerance ?? 0.3,
          tradeExpirySeconds:
            configuration.virtualTrading?.tradeExpirySeconds ?? 30,
          minimumObservationForTrendSeconds:
            configuration.virtualTrading?.minimumObservationForTrendSeconds ??
            30,
          confidenceBonusAfterSeconds1:
            configuration.virtualTrading?.confidenceBonusAfterSeconds1 ?? 20,
          confidenceBonusAfterSeconds2:
            configuration.virtualTrading?.confidenceBonusAfterSeconds2 ?? 35,
          pullbackWarmupSeconds:
            configuration.virtualTrading?.pullbackWarmupSeconds ?? 8,
        },

        optimization: {
          enabled: configuration.optimization?.enabled ?? true,
          paperTradingOnly: configuration.optimization?.paperTradingOnly ?? true,
          mode: this.normalizeOptimizationMode(
            configuration.optimization?.mode ?? 0,
          ),
          timeBasedCandidateMinutes: Number(
            configuration.optimization?.timeBasedCandidateMinutes ?? 60,
          ),
          sendConfigurationEmail:
            configuration.optimization?.sendConfigurationEmail ?? true,
          sendDailyEmail: configuration.optimization?.sendDailyEmail ?? true,
          autoPromoteBestConfiguration:
            configuration.optimization?.autoPromoteBestConfiguration ?? true,

          pollIntervalSeconds: Number(
            configuration.optimization?.pollIntervalSeconds ?? 5,
          ),
          minimumCandidateMinutes: Number(
            configuration.optimization?.minimumCandidateMinutes ?? 5,
          ),
          noSignalTimeoutMinutes: Number(
            configuration.optimization?.noSignalTimeoutMinutes ?? 20,
          ),
          noVirtualConfirmationTimeoutMinutes: Number(
            configuration.optimization?.noVirtualConfirmationTimeoutMinutes ?? 12,
          ),
          noPaperTradeTimeoutMinutes: Number(
            configuration.optimization?.noPaperTradeTimeoutMinutes ?? 10,
          ),
          maxVirtualTradeDurationMinutes: Number(
            configuration.optimization?.maxVirtualTradeDurationMinutes ?? 5,
          ),
          maxPaperTradeDurationMinutes: Number(
            configuration.optimization?.maxPaperTradeDurationMinutes ?? 20,
          ),
          forceCloseTimedOutPaperTrades:
            configuration.optimization?.forceCloseTimedOutPaperTrades ?? true,
          maximumCandidateMinutes: Number(
            configuration.optimization?.maximumCandidateMinutes ?? 45,
          ),

          maximumCandidatesPerDay: Number(
            configuration.optimization?.maximumCandidatesPerDay ?? 20,
          ),
          minimumCompletedTradesForAcceptance: Number(
            configuration.optimization?.minimumCompletedTradesForAcceptance ?? 10,
          ),
          preferredCompletedTrades: Number(
            configuration.optimization?.preferredCompletedTrades ?? 20,
          ),
          minimumVirtualCandidatesForAnalysis: Number(
            configuration.optimization?.minimumVirtualCandidatesForAnalysis ?? 5,
          ),

          minimumNetProfit: Number(
            configuration.optimization?.minimumNetProfit ?? 0,
          ),
          minimumProfitFactor: Number(
            configuration.optimization?.minimumProfitFactor ?? 1.2,
          ),
          maximumDrawdownPercent: Number(
            configuration.optimization?.maximumDrawdownPercent ?? 10,
          ),

          dailyEmailDelayMinutes: Number(
            configuration.optimization?.dailyEmailDelayMinutes ?? 5,
          ),
          validationHistoryFile:
            configuration.optimization?.validationHistoryFile ??
            'OptimizationRuns/validation-configuration-history.json',
          reportDirectory:
            configuration.optimization?.reportDirectory ?? 'OptimizationRuns',
        },

        exit: {
          atrExitMultiplier: configuration.exit?.atrExitMultiplier ?? 0.4,
          minimumProfitPercent:
            configuration.exit?.minimumProfitPercent ?? 0.25,
          trailingActivationNetProfit:
            configuration.exit?.trailingActivationNetProfit ?? 0,
          trailingStopAtrMultiplier:
            configuration.exit?.trailingStopAtrMultiplier ?? 0.6,
        },
        confidence: {
          maximumGainBonus: configuration.confidence?.maximumGainBonus ?? 15,
          gainBonusMultiplier:
            configuration.confidence?.gainBonusMultiplier ?? 20,
          strongPositiveTickRatio:
            configuration.confidence?.strongPositiveTickRatio ?? 3,
          mediumPositiveTickRatio:
            configuration.confidence?.mediumPositiveTickRatio ?? 2,
          strongTickBonus: configuration.confidence?.strongTickBonus ?? 5,
          mediumTickBonus: configuration.confidence?.mediumTickBonus ?? 3,
          weakTickBonus: configuration.confidence?.weakTickBonus ?? 1,
          maximumPositiveStreakBonus:
            configuration.confidence?.maximumPositiveStreakBonus ?? 5,
          maximumHigherHighBonus:
            configuration.confidence?.maximumHigherHighBonus ?? 5,
          drawdownPenaltyMultiplier:
            configuration.confidence?.drawdownPenaltyMultiplier ?? 5,
          idealMinimumRSI: configuration.confidence?.idealMinimumRSI ?? 55,
          idealMaximumRSI: configuration.confidence?.idealMaximumRSI ?? 65,
          overboughtRSI: configuration.confidence?.overboughtRSI ?? 70,
          oversoldRSI: configuration.confidence?.oversoldRSI ?? 50,
          macdBullishBonus: configuration.confidence?.macdBullishBonus ?? 4,
          macdBearishPenalty:
            configuration.confidence?.macdBearishPenalty ?? -6,
          macdHistogramBonus: configuration.confidence?.macdHistogramBonus ?? 2,
          macdHistogramPenalty:
            configuration.confidence?.macdHistogramPenalty ?? -2,
          highVolumeMultiplier:
            configuration.confidence?.highVolumeMultiplier ?? 3,
          mediumVolumeMultiplier:
            configuration.confidence?.mediumVolumeMultiplier ?? 2,
          lowVolumeMultiplier:
            configuration.confidence?.lowVolumeMultiplier ?? 1.5,
          highVolumeBonus: configuration.confidence?.highVolumeBonus ?? 3,
          mediumVolumeBonus: configuration.confidence?.mediumVolumeBonus ?? 2,
          lowVolumeBonus: configuration.confidence?.lowVolumeBonus ?? 1,
          wideBandwidth: configuration.confidence?.wideBandwidth ?? 3,
          narrowBandwidth: configuration.confidence?.narrowBandwidth ?? 1,
          wideBandwidthBonus: configuration.confidence?.wideBandwidthBonus ?? 3,
          narrowBandwidthPenalty:
            configuration.confidence?.narrowBandwidthPenalty ?? -4,
          observationBonus1: configuration.confidence?.observationBonus1 ?? 2,
          observationBonus2: configuration.confidence?.observationBonus2 ?? 2,
          movementWeight: configuration.confidence?.movementWeight ?? 0.15,
          trendStrengthWeight:
            configuration.confidence?.trendStrengthWeight ?? 0.05,
          trendStabilityWeight:
            configuration.confidence?.trendStabilityWeight ?? 0.05,
          recoveryWeight: configuration.confidence?.recoveryWeight ?? 0.03,
          breakoutWeight: configuration.confidence?.breakoutWeight ?? 0.02,
        },
        reporting: {
          strongBuyConfidence:
            configuration.reporting?.strongBuyConfidence ?? 95,
          buyConfidence: configuration.reporting?.buyConfidence ?? 90,
          watchConfidence: configuration.reporting?.watchConfidence ?? 85,
          movementScoreThreshold:
            configuration.reporting?.movementScoreThreshold ?? 90,
          trendStrengthThreshold:
            configuration.reporting?.trendStrengthThreshold ?? 80,
          trendStabilityThreshold:
            configuration.reporting?.trendStabilityThreshold ?? 80,
          recoveryScoreThreshold:
            configuration.reporting?.recoveryScoreThreshold ?? 80,
          higherHighCountThreshold:
            configuration.reporting?.higherHighCountThreshold ?? 5,
          volumeMultiplierThreshold:
            configuration.reporting?.volumeMultiplierThreshold ?? 2,
          maxDrawdownPercentThreshold:
            configuration.reporting?.maxDrawdownPercentThreshold ?? 0.25,
          volatilityScoreThreshold:
            configuration.reporting?.volatilityScoreThreshold ?? 60,
          riskRewardThreshold:
            configuration.reporting?.riskRewardThreshold ?? 2,
        },
      },
      {
        emitEvent: false,
      },
    );

    this.profileDrafts = {
      Equity: this.normalizeProfile(configuration.equity ?? this.buildProfileFromLegacy(configuration, 'Equity'), 'Equity'),
      Futures: this.normalizeProfile(configuration.futures ?? this.buildProfileFromLegacy(configuration, 'Futures'), 'Futures') as FuturesTradingSettings,
      Options: this.normalizeProfile(configuration.options ?? this.buildProfileFromLegacy(configuration, 'Options'), 'Options') as OptionsTradingSettings,
    };

    this.patchActiveProfile(this.selectedInstrumentType);
    this.activeInstrumentType = this.selectedInstrumentType;
    this.form.markAsPristine();
  }

  // ======================================================
  // Helpers
  // ======================================================

  private buildProfileFromLegacy(
    configuration: TradingConfiguration,
    type: InstrumentType,
  ): InstrumentTradingSettings {
    const source = type === 'Options' ? configuration.options : type === 'Futures' ? configuration.futures : configuration.equity;
    return {
      exchange: source?.exchange ?? (type === 'Equity' ? 'NSE' : 'NFO'),
      productType: source?.productType ?? 'INTRADAY',
      orderType: source?.orderType ?? 'MARKET',
      duration: source?.duration ?? 'DAY',
      minimumPrice: source?.minimumPrice ?? configuration.minPrice ?? (type === 'Equity' ? 50 : 0),
      minimumVolume: source?.minimumVolume ?? configuration.minVolume ?? (type === 'Equity' ? 500000 : 0),
      atrStopMultiplier: source?.atrStopMultiplier ?? 1.2,
      atrTargetMultiplier: source?.atrTargetMultiplier ?? 2.4,
      maximumStopPercent: source?.maximumStopPercent ?? 1.5,
      minimumStopPercent: source?.minimumStopPercent ?? 0.5,
      minimumRiskReward: source?.minimumRiskReward ?? 1.5,
      allowLong: source?.allowLong ?? true,
      allowShort: source?.allowShort ?? true,
      riskPercentage: source?.riskPercentage ?? configuration.riskPercentage ?? 2,
      maxCapitalPerTrade: source?.maxCapitalPerTrade ?? configuration.maxCapitalPerTrade ?? 10000,
      minimumNetProfit: source?.minimumNetProfit ?? configuration.minimumNetProfit ?? 5,
      minimumRoiPercent: source?.minimumRoiPercent ?? configuration.minimumRoiPercent ?? 0.3,
      maximumChargesPerTrade: source?.maximumChargesPerTrade ?? configuration.maximumChargesPerTrade ?? 100,
      minimumConfidence: source?.minimumConfidence ?? 65,
      minimumFinalScore: source?.minimumFinalScore ?? configuration.validation?.minimumFinalScore ?? 70,
      exitOrderTimeoutSeconds: source?.exitOrderTimeoutSeconds ?? 10,
      maxMarketDataAgeSeconds: source?.maxMarketDataAgeSeconds ?? 15,
      maximumExitRetries: source?.maximumExitRetries ?? 5,
      maximumSpreadPercent: source?.maximumSpreadPercent ?? 1.5,
      maximumSpreadAmount: source?.maximumSpreadAmount ?? 5,
      minimumBid: source?.minimumBid ?? 0,
      minimumAsk: source?.minimumAsk ?? 0,
      greeksCacheSeconds: source?.greeksCacheSeconds ?? 15,
      maximumOpenPositions: source?.maximumOpenPositions ?? 3,
      maximumRiskPerUnderlying: source?.maximumRiskPerUnderlying ?? 2500,
      maximumPositionsPerUnderlying: source?.maximumPositionsPerUnderlying ?? 1,
      maximumLotsPerTrade: source?.maximumLotsPerTrade ?? 0,
      maximumMarginUtilizationPercent: source?.maximumMarginUtilizationPercent ?? 70,
      forceSquareOffBuffer: source?.forceSquareOffBuffer ?? '00:15:00',
      evaluation: source?.evaluation ?? configuration.evaluation,
      validation: source?.validation ?? configuration.validation,
    };
  }

  private normalizeProfile(profile: InstrumentTradingSettings, type: InstrumentType): InstrumentTradingSettings {
    const base = {
      exchange: this.defaultExchange(type), productType: 'INTRADAY', orderType: 'MARKET', duration: 'DAY',
      minimumPrice: type === 'Equity' ? 50 : 0, minimumVolume: type === 'Equity' ? 500000 : 0,
      atrStopMultiplier: 1.2, atrTargetMultiplier: 2.4, maximumStopPercent: 1.5, minimumStopPercent: 0.5, minimumRiskReward: 1.5,
      allowLong: true, allowShort: true, riskPercentage: 2, maxCapitalPerTrade: 10000, minimumNetProfit: 5, minimumRoiPercent: 0.3,
      maximumChargesPerTrade: 100, minimumConfidence: 65, minimumFinalScore: 70, exitOrderTimeoutSeconds: 10, maxMarketDataAgeSeconds: 15,
      maximumExitRetries: 5, maximumSpreadPercent: 1.5, maximumSpreadAmount: 5, minimumBid: 0, minimumAsk: 0, greeksCacheSeconds: 15,
      maximumOpenPositions: 3, maximumRiskPerUnderlying: 2500, maximumPositionsPerUnderlying: 1, maximumLotsPerTrade: 0,
      maximumMarginUtilizationPercent: 70, forceSquareOffBuffer: '00:15:00',
    };
    const normalized = {
      ...base,
      ...profile,
      exchange: this.normalizeExchange(type, profile.exchange),
      evaluation: profile.evaluation ?? this.form?.controls?.evaluation?.value,
      validation: profile.validation ?? this.form?.controls?.validation?.value,
    };
    return normalized as InstrumentTradingSettings;
  }

  private patchActiveProfile(type: InstrumentType): void {
    const profile = this.profileDrafts[type];
    if (!profile) return;

    const extra = profile as Partial<FuturesTradingSettings & OptionsTradingSettings>;
    const exchange = this.normalizeExchange(type, profile.exchange);
    if (profile.exchange !== exchange) {
      profile.exchange = exchange;
    }

    this.form.patchValue({
      exchange,
      productType: profile.productType,
      orderType: profile.orderType,
      duration: profile.duration,
      minimumPrice: profile.minimumPrice,
      minimumVolume: profile.minimumVolume,
      atrStopMultiplier: profile.atrStopMultiplier,
      atrTargetMultiplier: profile.atrTargetMultiplier,
      maximumStopPercent: profile.maximumStopPercent,
      minimumStopPercent: profile.minimumStopPercent,
      minimumRiskReward: profile.minimumRiskReward,
      allowLong: profile.allowLong,
      allowShort: profile.allowShort,
      riskPercentage: profile.riskPercentage,
      maxCapitalPerTrade: profile.maxCapitalPerTrade,
      minimumNetProfit: profile.minimumNetProfit,
      minimumRoiPercent: profile.minimumRoiPercent,
      maximumChargesPerTrade: profile.maximumChargesPerTrade,
      maximumMarginUtilizationPercent: profile.maximumMarginUtilizationPercent,
      exitOrderTimeoutSeconds: profile.exitOrderTimeoutSeconds,
      maxMarketDataAgeSeconds: profile.maxMarketDataAgeSeconds,
      maximumExitRetries: profile.maximumExitRetries,
      maximumSpreadPercent: profile.maximumSpreadPercent,
      maximumSpreadAmount: profile.maximumSpreadAmount,
      minimumBid: profile.minimumBid,
      minimumAsk: profile.minimumAsk,
      greeksCacheSeconds: profile.greeksCacheSeconds,
      maximumOpenPositions: profile.maximumOpenPositions,
      maximumRiskPerUnderlying: profile.maximumRiskPerUnderlying,
      maximumPositionsPerUnderlying: profile.maximumPositionsPerUnderlying,
      maximumLotsPerTrade: profile.maximumLotsPerTrade,
      forceSquareOffBufferMinutes: this.toMinutes(profile.forceSquareOffBuffer),
      validation: profile.validation,
      evaluation: profile.evaluation,
      futuresExpiryType: extra.expiryType,
      futuresMinimumOpenInterest: extra.minimumOpenInterest,
      futuresMinimumOIChangePercent: extra.minimumOIChangePercent,
      futuresMaximumDailyLoss: extra.maximumDailyLoss,
      futuresMaximumDailyTrades: extra.maximumDailyTrades,
      optionsOptionSide: extra.optionSide,
      optionsExpiryType: extra.expiryType,
      optionsStrikeStepsFromAtm: extra.strikeStepsFromAtm,
      optionsContractsPerUnderlying: extra.contractsPerUnderlying,
      optionsMinimumOpenInterest: extra.minimumOpenInterest,
      optionsMinimumDelta: extra.minimumDelta,
      optionsMaximumDelta: extra.maximumDelta,
      optionsMaximumAbsoluteTheta: extra.maximumAbsoluteTheta,
      optionsMaximumImpliedVolatility: extra.maximumImpliedVolatility,
      optionsMinimumGamma: extra.minimumGamma,
      optionsMinimumVega: extra.minimumVega,
      optionsStrikeInterval: extra.strikeInterval,
      optionsMinimumOptionVolume: extra.minimumOptionVolume,
      optionsMinimumTurnover: extra.minimumTurnover,
      optionsMinimumPremium: extra.minimumPremium,
      optionsMaximumPremium: extra.maximumPremium,
      optionsMinimumOIChangePercent: extra.minimumOIChangePercent,
      optionsAllowExpiryDayTrading: extra.allowExpiryDayTrading,
      optionsMinimumMinutesBeforeExpiry: extra.minimumMinutesBeforeExpiry,
      optionsMaximumExpiryDayIV: extra.maximumExpiryDayIV,
      optionsMaximumDailyLoss: extra.maximumDailyLoss,
      optionsMaximumDailyTrades: extra.maximumDailyTrades,
      optionsExpiryMarketCloseTime: this.toTimeInput(extra.expiryMarketCloseTime),
      optionsRequireMarketDepth: extra.requireMarketDepth,
      optionsMaximumBidAskSpreadPercent: extra.maximumBidAskSpreadPercent,
      optionsMaximumBidAskSpreadAmount: extra.maximumBidAskSpreadAmount,
      optionsMinimumOptionTurnover: extra.minimumOptionTurnover,
      optionsRequireFreshGreeks: extra.requireFreshGreeks,
      optionsGreeksFreshnessSeconds: extra.greeksFreshnessSeconds,
      optionsMaximumStrikeCandidatesPerSide: extra.maximumStrikeCandidatesPerSide,
      optionsMinimumCallScore: extra.minimumCallScore,
      optionsMinimumPutScore: extra.minimumPutScore,
      optionsMinimumPCR: extra.minimumPCR,
      optionsMaximumPCR: extra.maximumPCR,
      optionsUseUnderlyingMultiTimeframeTrend: extra.useUnderlyingMultiTimeframeTrend,
      optionsTradeMode: extra.tradeMode,
      optionsAllowNakedWriting: extra.allowNakedWriting,
      optionsAllowNakedCallWriting: extra.allowNakedCallWriting,
      optionsAllowNakedPutWriting: extra.allowNakedPutWriting,
      optionsAllowNakedWritingOnExpiryDay: extra.allowNakedWritingOnExpiryDay,
      optionsShortMinimumDelta: extra.shortMinimumDelta,
      optionsShortMaximumDelta: extra.shortMaximumDelta,
      optionsShortMinimumIV: extra.shortMinimumIV,
      optionsShortMaximumIV: extra.shortMaximumIV,
      optionsShortMinimumThetaAbs: extra.shortMinimumThetaAbs,
      optionsShortMaximumThetaAbs: extra.shortMaximumThetaAbs,
      optionsShortMinimumPremium: extra.shortMinimumPremium,
      optionsShortMaximumPremium: extra.shortMaximumPremium,
      optionsMinimumShortCallScore: extra.minimumShortCallScore,
      optionsMinimumShortPutScore: extra.minimumShortPutScore,
      optionsMaximumNakedOptionRiskPerTrade: extra.maximumNakedOptionRiskPerTrade,
      optionsMaximumNakedOptionLotsPerTrade: extra.maximumNakedOptionLotsPerTrade,
      optionsNakedOptionMarginSafetyMultiplier: extra.nakedOptionMarginSafetyMultiplier,
      optionsMaximumUnderlyingDeltaExposure: extra.maximumUnderlyingDeltaExposure,
      optionsMaximumExpiryDayRiskMultiplier: extra.maximumExpiryDayRiskMultiplier,
      optionsNakedStressUnderlyingMovePercent: extra.nakedStressUnderlyingMovePercent,
      optionsNakedStressIVIncreasePercent: extra.nakedStressIVIncreasePercent,
      optionsMaximumNakedStressLossPerTrade: extra.maximumNakedStressLossPerTrade,
      optionsMaximumUnderlyingStressLoss: extra.maximumUnderlyingStressLoss,
      optionsMaximumOpenDeltaExposure: extra.maximumOpenDeltaExposure,
      optionsMaximumOpenGammaExposure: extra.maximumOpenGammaExposure,
      optionsMaximumOpenVegaExposure: extra.maximumOpenVegaExposure,
      optionsMaximumShortLotsPerExpiry: extra.maximumShortLotsPerExpiry,
      optionsMaximumShortLotsPerUnderlying: extra.maximumShortLotsPerUnderlying,
      optionsMaximumShortLotsPerStrike: extra.maximumShortLotsPerStrike,
      optionsMaximumShortPremiumExposure: extra.maximumShortPremiumExposure,
      optionsAllowNakedStrangle: extra.allowNakedStrangle,
      optionsAllowNakedStraddle: extra.allowNakedStraddle,
      optionsEmergencyDeltaExposure: extra.emergencyDeltaExposure,
      optionsEmergencyGammaExposure: extra.emergencyGammaExposure,
      optionsEmergencyVegaExposure: extra.emergencyVegaExposure,
      optionsEmergencyIVIncreasePercent: extra.emergencyIVIncreasePercent,
      optionsEmergencyStressLoss: extra.emergencyStressLoss,
      optionsEmergencyMarginUtilizationPercent: extra.emergencyMarginUtilizationPercent,
      optionsNakedRiskMonitorSeconds: extra.nakedRiskMonitorSeconds,
      optionsMaximumRiskPerTrade: extra.maximumRiskPerTrade,
    }, { emitEvent: false });
  }

  onInstrumentTypeChanged(type: InstrumentType): void {
    const previous = this.activeInstrumentType;

    if (previous !== type && this.profileDrafts[previous]) {
      this.profileDrafts[previous] = this.readActiveProfile(this.profileDrafts[previous]!);
    }

    // Always normalize the destination profile before displaying it.
    // This prevents an Equity exchange such as NSE/BSE from leaking into
    // an F&O profile when the instrument type changes.
    const profile = this.profileDrafts[type];
    if (profile) {
      profile.exchange = this.normalizeExchange(type, profile.exchange);
    }

    this.patchActiveProfile(type);
    this.activeInstrumentType = type;
    this.form.markAsDirty();
  }

  private readActiveProfile(existing: InstrumentTradingSettings): InstrumentTradingSettings {
    const value = this.form.getRawValue() as any;
    const base: InstrumentTradingSettings = {
      ...existing,
      exchange: String(value.exchange ?? existing.exchange),
      productType: String(value.productType ?? existing.productType),
      orderType: String(value.orderType ?? existing.orderType),
      duration: String(value.duration ?? existing.duration),
      minimumPrice: Number(value.minimumPrice),
      minimumVolume: Number(value.minimumVolume),
      atrStopMultiplier: Number(value.atrStopMultiplier),
      atrTargetMultiplier: Number(value.atrTargetMultiplier),
      maximumStopPercent: Number(value.maximumStopPercent),
      minimumStopPercent: Number(value.minimumStopPercent),
      minimumRiskReward: Number(value.minimumRiskReward),
      allowLong: !!value.allowLong,
      allowShort: !!value.allowShort,
      riskPercentage: Number(value.riskPercentage),
      maxCapitalPerTrade: Number(value.maxCapitalPerTrade),
      minimumNetProfit: Number(value.minimumNetProfit),
      minimumRoiPercent: Number(value.minimumRoiPercent),
      maximumChargesPerTrade: Number(value.maximumChargesPerTrade),
      minimumConfidence: Number(value.validation?.minimumConfidence ?? existing.minimumConfidence),
      minimumFinalScore: Number(value.validation?.minimumFinalScore ?? existing.minimumFinalScore),
      exitOrderTimeoutSeconds: Number(value.exitOrderTimeoutSeconds),
      maxMarketDataAgeSeconds: Number(value.maxMarketDataAgeSeconds),
      maximumExitRetries: Number(value.maximumExitRetries),
      maximumSpreadPercent: Number(value.maximumSpreadPercent),
      maximumSpreadAmount: Number(value.maximumSpreadAmount),
      minimumBid: Number(value.minimumBid),
      minimumAsk: Number(value.minimumAsk),
      greeksCacheSeconds: Number(value.greeksCacheSeconds),
      maximumOpenPositions: Number(value.maximumOpenPositions),
      maximumRiskPerUnderlying: Number(value.maximumRiskPerUnderlying),
      maximumPositionsPerUnderlying: Number(value.maximumPositionsPerUnderlying),
      maximumLotsPerTrade: Number(value.maximumLotsPerTrade),
      maximumMarginUtilizationPercent: Number(value.maximumMarginUtilizationPercent),
      forceSquareOffBuffer: this.minutesToTimeSpan(value.forceSquareOffBufferMinutes),
      evaluation: value.evaluation,
      validation: value.validation,
    };

    if (this.selectedInstrumentType === 'Futures') {
      return { ...base, expiryType: value.futuresExpiryType, minimumOpenInterest: Number(value.futuresMinimumOpenInterest), minimumOIChangePercent: Number(value.futuresMinimumOIChangePercent), maximumDailyLoss: Number(value.futuresMaximumDailyLoss), maximumDailyTrades: Number(value.futuresMaximumDailyTrades) } as FuturesTradingSettings;
    }

    if (this.selectedInstrumentType === 'Options') {
      return { ...base, optionSide: value.optionsOptionSide, expiryType: value.optionsExpiryType, strikeStepsFromAtm: Number(value.optionsStrikeStepsFromAtm), contractsPerUnderlying: Number(value.optionsContractsPerUnderlying), minimumOpenInterest: Number(value.optionsMinimumOpenInterest), minimumDelta: Number(value.optionsMinimumDelta), maximumDelta: Number(value.optionsMaximumDelta), maximumAbsoluteTheta: Number(value.optionsMaximumAbsoluteTheta), maximumImpliedVolatility: Number(value.optionsMaximumImpliedVolatility), minimumGamma: Number(value.optionsMinimumGamma), minimumVega: Number(value.optionsMinimumVega), strikeInterval: Number(value.optionsStrikeInterval), minimumOptionVolume: Number(value.optionsMinimumOptionVolume), minimumTurnover: Number(value.optionsMinimumTurnover), minimumPremium: Number(value.optionsMinimumPremium), maximumPremium: Number(value.optionsMaximumPremium), minimumOIChangePercent: Number(value.optionsMinimumOIChangePercent), allowExpiryDayTrading: !!value.optionsAllowExpiryDayTrading, minimumMinutesBeforeExpiry: Number(value.optionsMinimumMinutesBeforeExpiry), maximumExpiryDayIV: Number(value.optionsMaximumExpiryDayIV), maximumDailyLoss: Number(value.optionsMaximumDailyLoss), maximumDailyTrades: Number(value.optionsMaximumDailyTrades), expiryMarketCloseTime: this.toTimeSpan(value.optionsExpiryMarketCloseTime), requireMarketDepth: !!value.optionsRequireMarketDepth, maximumBidAskSpreadPercent: Number(value.optionsMaximumBidAskSpreadPercent), maximumBidAskSpreadAmount: Number(value.optionsMaximumBidAskSpreadAmount), minimumOptionTurnover: Number(value.optionsMinimumOptionTurnover), requireFreshGreeks: !!value.optionsRequireFreshGreeks, greeksFreshnessSeconds: Number(value.optionsGreeksFreshnessSeconds), maximumStrikeCandidatesPerSide: Number(value.optionsMaximumStrikeCandidatesPerSide), minimumCallScore: Number(value.optionsMinimumCallScore), minimumPutScore: Number(value.optionsMinimumPutScore), minimumPCR: Number(value.optionsMinimumPCR), maximumPCR: Number(value.optionsMaximumPCR), useUnderlyingMultiTimeframeTrend: !!value.optionsUseUnderlyingMultiTimeframeTrend, tradeMode: value.optionsTradeMode, allowNakedWriting: !!value.optionsAllowNakedWriting, allowNakedCallWriting: !!value.optionsAllowNakedCallWriting, allowNakedPutWriting: !!value.optionsAllowNakedPutWriting, allowNakedWritingOnExpiryDay: !!value.optionsAllowNakedWritingOnExpiryDay, shortMinimumDelta: Number(value.optionsShortMinimumDelta), shortMaximumDelta: Number(value.optionsShortMaximumDelta), shortMinimumIV: Number(value.optionsShortMinimumIV), shortMaximumIV: Number(value.optionsShortMaximumIV), shortMinimumThetaAbs: Number(value.optionsShortMinimumThetaAbs), shortMaximumThetaAbs: Number(value.optionsShortMaximumThetaAbs), shortMinimumPremium: Number(value.optionsShortMinimumPremium), shortMaximumPremium: Number(value.optionsShortMaximumPremium), minimumShortCallScore: Number(value.optionsMinimumShortCallScore), minimumShortPutScore: Number(value.optionsMinimumShortPutScore), maximumNakedOptionRiskPerTrade: Number(value.optionsMaximumNakedOptionRiskPerTrade), maximumNakedOptionLotsPerTrade: Number(value.optionsMaximumNakedOptionLotsPerTrade), nakedOptionMarginSafetyMultiplier: Number(value.optionsNakedOptionMarginSafetyMultiplier), maximumUnderlyingDeltaExposure: Number(value.optionsMaximumUnderlyingDeltaExposure), maximumExpiryDayRiskMultiplier: Number(value.optionsMaximumExpiryDayRiskMultiplier), nakedStressUnderlyingMovePercent: Number(value.optionsNakedStressUnderlyingMovePercent), nakedStressIVIncreasePercent: Number(value.optionsNakedStressIVIncreasePercent), maximumNakedStressLossPerTrade: Number(value.optionsMaximumNakedStressLossPerTrade), maximumUnderlyingStressLoss: Number(value.optionsMaximumUnderlyingStressLoss), maximumOpenDeltaExposure: Number(value.optionsMaximumOpenDeltaExposure), maximumOpenGammaExposure: Number(value.optionsMaximumOpenGammaExposure), maximumOpenVegaExposure: Number(value.optionsMaximumOpenVegaExposure), maximumShortLotsPerExpiry: Number(value.optionsMaximumShortLotsPerExpiry), maximumShortLotsPerUnderlying: Number(value.optionsMaximumShortLotsPerUnderlying), maximumShortLotsPerStrike: Number(value.optionsMaximumShortLotsPerStrike), maximumShortPremiumExposure: Number(value.optionsMaximumShortPremiumExposure), allowNakedStrangle: !!value.optionsAllowNakedStrangle, allowNakedStraddle: !!value.optionsAllowNakedStraddle, emergencyDeltaExposure: Number(value.optionsEmergencyDeltaExposure), emergencyGammaExposure: Number(value.optionsEmergencyGammaExposure), emergencyVegaExposure: Number(value.optionsEmergencyVegaExposure), emergencyIVIncreasePercent: Number(value.optionsEmergencyIVIncreasePercent), emergencyStressLoss: Number(value.optionsEmergencyStressLoss), emergencyMarginUtilizationPercent: Number(value.optionsEmergencyMarginUtilizationPercent), nakedRiskMonitorSeconds: Number(value.optionsNakedRiskMonitorSeconds), maximumRiskPerTrade: Number(value.optionsMaximumRiskPerTrade) } as OptionsTradingSettings;
    }

    return base;
  }

  private toMinutes(value: string | undefined): number {
    if (!value) return 15;
    const parts = value.split(':').map(Number);
    if (parts.some((part) => !Number.isFinite(part))) return 15;
    return (parts[0] || 0) * 60 + (parts[1] || 0);
  }

  /**
   * Convert the UI's total-minute value to the API's TimeSpan format.
   * Examples: 15 -> 00:15:00, 90 -> 01:30:00, 900 -> 15:00:00.
   */
  private minutesToTimeSpan(value: number | string | null | undefined): string {
    const minutes = Number(value);
    if (!Number.isFinite(minutes) || minutes < 0) {
      return '00:15:00';
    }

    const totalMinutes = Math.round(minutes);
    const hours = Math.floor(totalMinutes / 60);
    const mins = totalMinutes % 60;

    return `${String(hours).padStart(2, '0')}:${String(mins).padStart(2, '0')}:00`;
  }

  private toTimeInput(value: string | null | undefined): string {
    if (!value) {
      return '';
    }

    return value.substring(0, 5);
  }

  normalizeOptimizationMode(
    value: OptimizationMode | string | number | null | undefined,
  ): 0 | 1 {
    if (value === 'TimeBased' || value === 1 || value === '1') {
      return 1;
    }

    return 0;
  }

  optimizationModeLabel(
    value: OptimizationMode | string | number | null | undefined,
  ): string {
    return this.normalizeOptimizationMode(value) === 1
      ? 'Time Based'
      : 'Count Based';
  }

  private normalizeStrategy(
    value: TradingStrategy | string | number,
  ): TradingStrategy {
    if (typeof value === 'string') {
      const numeric = Number(value);

      if (!Number.isNaN(numeric)) {
        return numeric as TradingStrategy;
      }

      return TradingStrategy[value as keyof typeof TradingStrategy];
    }

    return value as TradingStrategy;
  }

  private toTimeSpan(value: string | null | undefined): string {
    if (!value) {
      return '00:00:00';
    }

    const parts = String(value).trim().split(':').map(Number);
    if (parts.some(Number.isNaN) || parts.length < 2) {
      return '00:00:00';
    }

    const hours = Math.max(0, Math.floor(parts[0] || 0));
    const minutes = Math.max(0, Math.floor(parts[1] || 0));
    const seconds = Math.max(0, Math.floor(parts[2] || 0));

    // Normalize overflow instead of producing invalid values such as 900:00.
    const totalSeconds = hours * 3600 + minutes * 60 + seconds;
    const normalizedHours = Math.floor(totalSeconds / 3600);
    const normalizedMinutes = Math.floor((totalSeconds % 3600) / 60);
    const normalizedSeconds = totalSeconds % 60;

    return `${String(normalizedHours).padStart(2, '0')}:${String(normalizedMinutes).padStart(2, '0')}:${String(normalizedSeconds).padStart(2, '0')}`;
  }

  private parseExcludedSymbols(value: string | null | undefined): string[] {
    return (value ?? '')
      .split(',')
      .map((symbol) => symbol.trim().toUpperCase())
      .filter(Boolean);
  }

  // ======================================================
  // Getters
  // ======================================================

  get isDirty(): boolean {
    return this.form.dirty;
  }

  get isValid(): boolean {
    return this.form.valid;
  }

  get controls() {
    return this.form.controls;
  }

  // ======================================================
  // Reset
  // ======================================================

  reset(): void {
    this.loadConfiguration();
  }

  // ======================================================
  // Save
  // ======================================================

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();

      return;
    }

    this.saving = true;

    const value = this.form.getRawValue();

    this.profileDrafts[this.selectedInstrumentType] = this.readActiveProfile(this.normalizeProfile(this.profileDrafts[this.selectedInstrumentType] ?? this.buildProfileFromLegacy(this.#angel.configuration() ?? ({ id: 'DEFAULT' } as TradingConfiguration), this.selectedInstrumentType), this.selectedInstrumentType));

    const configuration: TradingConfiguration = {
      id: 'DEFAULT',
      optimization: {
        enabled: value.optimization?.enabled ?? true,
        paperTradingOnly: value.optimization?.paperTradingOnly ?? true,
        mode: this.normalizeOptimizationMode(value.optimization?.mode ?? 0),
        timeBasedCandidateMinutes: Number(
          value.optimization?.timeBasedCandidateMinutes ?? 60,
        ),
        sendConfigurationEmail:
          value.optimization?.sendConfigurationEmail ?? true,
        sendDailyEmail: value.optimization?.sendDailyEmail ?? true,
        autoPromoteBestConfiguration:
          value.optimization?.autoPromoteBestConfiguration ?? true,

        pollIntervalSeconds: Number(value.optimization?.pollIntervalSeconds ?? 5),
        minimumCandidateMinutes: Number(
          value.optimization?.minimumCandidateMinutes ?? 5,
        ),
        noSignalTimeoutMinutes: Number(
          value.optimization?.noSignalTimeoutMinutes ?? 20,
        ),
        noVirtualConfirmationTimeoutMinutes: Number(
          value.optimization?.noVirtualConfirmationTimeoutMinutes ?? 12,
        ),
        noPaperTradeTimeoutMinutes: Number(
          value.optimization?.noPaperTradeTimeoutMinutes ?? 10,
        ),
        maxVirtualTradeDurationMinutes: Number(
          value.optimization?.maxVirtualTradeDurationMinutes ?? 5,
        ),
        maxPaperTradeDurationMinutes: Number(
          value.optimization?.maxPaperTradeDurationMinutes ?? 20,
        ),
        forceCloseTimedOutPaperTrades:
          value.optimization?.forceCloseTimedOutPaperTrades ?? true,
        maximumCandidateMinutes: Number(
          value.optimization?.maximumCandidateMinutes ?? 45,
        ),

        maximumCandidatesPerDay: Number(
          value.optimization?.maximumCandidatesPerDay ?? 20,
        ),
        minimumCompletedTradesForAcceptance: Number(
          value.optimization?.minimumCompletedTradesForAcceptance ?? 10,
        ),
        preferredCompletedTrades: Number(
          value.optimization?.preferredCompletedTrades ?? 20,
        ),
        minimumVirtualCandidatesForAnalysis: Number(
          value.optimization?.minimumVirtualCandidatesForAnalysis ?? 5,
        ),

        minimumNetProfit: Number(value.optimization?.minimumNetProfit ?? 0),
        minimumProfitFactor: Number(
          value.optimization?.minimumProfitFactor ?? 1.2,
        ),
        maximumDrawdownPercent: Number(
          value.optimization?.maximumDrawdownPercent ?? 10,
        ),

        dailyEmailDelayMinutes: Number(
          value.optimization?.dailyEmailDelayMinutes ?? 5,
        ),
        validationHistoryFile:
          value.optimization?.validationHistoryFile ??
          'OptimizationRuns/validation-configuration-history.json',
        reportDirectory:
          value.optimization?.reportDirectory ?? 'OptimizationRuns',
      },


      instrumentType: this.selectedInstrumentType,
      equity: this.profileDrafts.Equity,
      futures: this.profileDrafts.Futures as FuturesTradingSettings,
      options: this.profileDrafts.Options as OptionsTradingSettings,

      enableAutoTrading: value.enableAutoTrading ?? false,

      paperTrading: value.paperTrading ?? false,

      enableNotification: value.enableNotification ?? false,

      strategy: value.strategy!,

      riskPercentage: Number(value.riskPercentage),

      maxCapitalPerTrade: Number(value.maxCapitalPerTrade),

      maxDailyLoss: Number(value.maxDailyLoss),

      maxDailyTrades: Number(value.maxDailyTrades),

      cooldownMinutes: Number(value.cooldownMinutes),

      ignoreMarketHours: value.ignoreMarketHours ?? false,

      marketOpenTime: this.toTimeSpan(value.marketOpenTime),

      marketCloseTime: this.toTimeSpan(value.marketCloseTime),

      excludedSymbols: this.parseExcludedSymbols(value.excludedSymbolsText),

      watchListRefreshMinutes: Number(value.watchListRefreshMinutes),

      minPrice: Number(value.minPrice ?? 50),

      minVolume: Number(value.minVolume ?? 500000),

      maxCandidates: Number(value.maxCandidates ?? 100),

      minimumRoiPercent: Number(value.minimumRoiPercent ?? 0.3),

      minimumNetProfit: Number(value.minimumNetProfit ?? 5),

      autoSquareOff: value.autoSquareOff ?? false,

      paperTradingBalance: Number(value.paperTradingBalance ?? 100000),

      virtualTradeObservationSeconds: Number(
        value.virtualTradeObservationSeconds ?? 20,
      ),

      virtualTradeExpirySeconds: Number(value.virtualTradeExpirySeconds ?? 60),

      minimumVirtualProfitPercent: Number(
        value.minimumVirtualProfitPercent ?? 0.25,
      ),

      maximumVirtualPullbackPercent: Number(
        value.maximumVirtualPullbackPercent ?? 0.5,
      ),

      visibleColumns: this.#angel.configuration()?.visibleColumns ?? [],

      maximumChargesPerTrade: Number(value.maximumChargesPerTrade ?? 100),
      lastDailySummarySent: null,
      buyTradingInterval: Number(value.buyTradingInterval ?? 5),
      sellTradingInterval: Number(value.sellTradingInterval ?? 1000),
      enableEMA9: value.enableEMA9 ?? true,
      enableEMA21: value.enableEMA21 ?? true,
      enableEMA50: value.enableEMA50 ?? true,
      enableEMA200: value.enableEMA200 ?? true,
      enableATR: value.enableATR ?? true,
      enableRSI: value.enableRSI ?? true,
      enableVWAP: value.enableVWAP ?? true,
      enableADX: value.enableADX ?? true,
      enableRelativeVolume: value.enableRelativeVolume ?? true,
      enableEMASlope: value.enableEMASlope ?? true,
      enableDistanceFromEMA: value.enableDistanceFromEMA ?? true,
      enableChoppiness: value.enableChoppiness ?? true,
      enableSuperTrend: value.enableSuperTrend ?? true,
      enableAnchoredVWAP: value.enableAnchoredVWAP ?? true,
      enableMACD: value.enableMACD ?? true,
      enableBollinger: value.enableBollinger ?? true,
      validation: {
        minimumMovementScore: Number(
          value.validation?.minimumMovementScore ?? 45,
        ),
        minimumConfidence: Number(value.validation?.minimumConfidence ?? 65),
        minimumRiskReward: Number(value.validation?.minimumRiskReward ?? 1.5),
        minimumTrendStrength: Number(
          value.validation?.minimumTrendStrength ?? 50,
        ),
        minimumTrendStability: Number(
          value.validation?.minimumTrendStability ?? 55,
        ),
        minimumRecoveryScore: Number(
          value.validation?.minimumRecoveryScore ?? 50,
        ),
        minimumVolatilityScore: Number(
          value.validation?.minimumVolatilityScore ?? 30,
        ),
        minimumNoiseScore: Number(value.validation?.minimumNoiseScore ?? 45),
        minimumBreakoutStrength: Number(
          value.validation?.minimumBreakoutStrength ?? 45,
        ),
        minimumRSI: Number(value.validation?.minimumRSI ?? 50),
        maximumRSI: Number(value.validation?.maximumRSI ?? 72),
        minimumVolumeMultiplier: Number(
          value.validation?.minimumVolumeMultiplier ?? 1.5,
        ),
        minimumPositiveTickRatio: Number(
          value.validation?.minimumPositiveTickRatio ?? 0.55,
        ),
        minimumAboveEntryRatio: Number(
          value.validation?.minimumAboveEntryRatio ?? 0.55,
        ),
        minimumHigherHighs: Number(value.validation?.minimumHigherHighs ?? 2),
        minimumConsecutivePositiveTicks: Number(
          value.validation?.minimumConsecutivePositiveTicks ?? 2,
        ),
        maximumConsecutiveNegativeTicks: Number(
          value.validation?.maximumConsecutiveNegativeTicks ?? 6,
        ),
        maximumDrawdownPercent: Number(
          value.validation?.maximumDrawdownPercent ?? 0.4,
        ),
        minimumRejectMovementScore: Number(
          value.validation?.minimumRejectMovementScore ?? 30,
        ),
        maximumRejectDrawdownPercent: Number(
          value.validation?.maximumRejectDrawdownPercent ?? 0.6,
        ),
        minimumRejectPositiveTickRatio: Number(
          value.validation?.minimumRejectPositiveTickRatio ?? 0.35,
        ),
        minimumRejectAboveEntryRatio: Number(
          value.validation?.minimumRejectAboveEntryRatio ?? 0.25,
        ),
        maximumRejectConsecutiveNegativeTicks: Number(
          value.validation?.maximumRejectConsecutiveNegativeTicks ?? 6,
        ),
        minimumRejectProfitPercent: Number(
          value.validation?.minimumRejectProfitPercent ?? 0.1,
        ),
        minimumGainPercent: Number(
          value.validation?.minimumGainPercent ?? 0.12,
        ),
        minimumPriceSlope: Number(value.validation?.minimumPriceSlope ?? 0.1),
        momentumMinimumPriceRatio: Number(
          value.validation?.momentumMinimumPriceRatio ?? 0.998,
        ),
        momentumMaximumDrawdown: Number(
          value.validation?.momentumMaximumDrawdown ?? 0.5,
        ),
        momentumHighestPriceTolerance: Number(
          value.validation?.momentumHighestPriceTolerance ?? 0.997,
        ),
        pullbackMinimumPriceRatio: Number(
          value.validation?.pullbackMinimumPriceRatio ?? 0.998,
        ),
        maximumPullbackGain: Number(
          value.validation?.maximumPullbackGain ?? 0.75,
        ),
        minimumFinalScore: Number(value.validation?.minimumFinalScore ?? 70),
        minimumBollingerBandwidth: Number(
          value.validation?.minimumBollingerBandwidth ?? 1,
        ),
        minimumFinalRSI: Number(value.validation?.minimumFinalRSI ?? 48),
        maximumFinalRSI: Number(value.validation?.maximumFinalRSI ?? 72),
      },
      evaluation: {
        strongAdx: Number(value.evaluation?.strongAdx ?? 30),
        mediumAdx: Number(value.evaluation?.mediumAdx ?? 25),
        lowChoppiness: Number(value.evaluation?.lowChoppiness ?? 38),
        highChoppiness: Number(value.evaluation?.highChoppiness ?? 55),
        highRelativeVolume: Number(value.evaluation?.highRelativeVolume ?? 2),
        mediumRelativeVolume: Number(
          value.evaluation?.mediumRelativeVolume ?? 1.5,
        ),
        lowRelativeVolume: Number(value.evaluation?.lowRelativeVolume ?? 1.2),
        excellentScore: Number(value.evaluation?.excellentScore ?? 90),
        goodScore: Number(value.evaluation?.goodScore ?? 80),
        averageScore: Number(value.evaluation?.averageScore ?? 70),
        ema9AboveEma21Score: Number(
          value.evaluation?.ema9AboveEma21Score ?? 10,
        ),
        ema21AboveEma50Score: Number(
          value.evaluation?.ema21AboveEma50Score ?? 5,
        ),
        superTrendBullishScore: Number(
          value.evaluation?.superTrendBullishScore ?? 5,
        ),
        priceAboveVwapScore: Number(value.evaluation?.priceAboveVwapScore ?? 5),
        anchoredVwapScore: Number(value.evaluation?.anchoredVwapScore ?? 5),
        strongAdxScore: Number(value.evaluation?.strongAdxScore ?? 10),
        mediumAdxScore: Number(value.evaluation?.mediumAdxScore ?? 6),
        plusDiAboveMinusDiScore: Number(
          value.evaluation?.plusDiAboveMinusDiScore ?? 5,
        ),
        lowChoppinessScore: Number(value.evaluation?.lowChoppinessScore ?? 5),
        momentumIncreasingScore: Number(
          value.evaluation?.momentumIncreasingScore ?? 8,
        ),
        pullbackIncreasingMomentumScore: Number(
          value.evaluation?.pullbackIncreasingMomentumScore ?? 8,
        ),
        lastCandleBullishScore: Number(
          value.evaluation?.lastCandleBullishScore ?? 5,
        ),
        higherHighScore: Number(value.evaluation?.higherHighScore ?? 3),
        higherLowScore: Number(value.evaluation?.higherLowScore ?? 3),
        ema9SlopePositiveScore: Number(
          value.evaluation?.ema9SlopePositiveScore ?? 2,
        ),
        ema21SlopePositiveScore: Number(
          value.evaluation?.ema21SlopePositiveScore ?? 2,
        ),
        highRelativeVolumeScore: Number(
          value.evaluation?.highRelativeVolumeScore ?? 15,
        ),
        mediumRelativeVolumeScore: Number(
          value.evaluation?.mediumRelativeVolumeScore ?? 10,
        ),
        lowRelativeVolumeScore: Number(
          value.evaluation?.lowRelativeVolumeScore ?? 5,
        ),
        pullbackHighRelativeVolumeScore: Number(
          value.evaluation?.pullbackHighRelativeVolumeScore ?? 10,
        ),
        pullbackMediumRelativeVolumeScore: Number(
          value.evaluation?.pullbackMediumRelativeVolumeScore ?? 7,
        ),
        pullbackLowRelativeVolumeScore: Number(
          value.evaluation?.pullbackLowRelativeVolumeScore ?? 4,
        ),
        momentumPullbackIdealMinimum: Number(
          value.evaluation?.momentumPullbackIdealMinimum ?? 0,
        ),
        momentumPullbackIdealMaximum: Number(
          value.evaluation?.momentumPullbackIdealMaximum ?? 1.5,
        ),
        momentumPullbackMaximum: Number(
          value.evaluation?.momentumPullbackMaximum ?? 2.5,
        ),
        momentumPullbackIdealScore: Number(
          value.evaluation?.momentumPullbackIdealScore ?? 10,
        ),
        momentumPullbackSecondaryScore: Number(
          value.evaluation?.momentumPullbackSecondaryScore ?? 5,
        ),
        pullbackDistanceMinimum: Number(
          value.evaluation?.pullbackDistanceMinimum ?? 0.3,
        ),
        pullbackDistanceIdealMaximum: Number(
          value.evaluation?.pullbackDistanceIdealMaximum ?? 1.2,
        ),
        pullbackDistanceMaximum: Number(
          value.evaluation?.pullbackDistanceMaximum ?? 2,
        ),
        pullbackDistanceIdealScore: Number(
          value.evaluation?.pullbackDistanceIdealScore ?? 10,
        ),
        pullbackDistanceSecondaryScore: Number(
          value.evaluation?.pullbackDistanceSecondaryScore ?? 5,
        ),
        ema9BounceScore: Number(value.evaluation?.ema9BounceScore ?? 5),
        vwapBounceScore: Number(value.evaluation?.vwapBounceScore ?? 5),
        rsiValidScore: Number(value.evaluation?.rsiValidScore ?? 10),
        rsiAboveMinimumScore: Number(
          value.evaluation?.rsiAboveMinimumScore ?? 5,
        ),
        rsiBelowMinimumPenalty: Number(
          value.evaluation?.rsiBelowMinimumPenalty ?? -15,
        ),
        momentumRsiBelowMinimumPenalty: Number(
          value.evaluation?.momentumRsiBelowMinimumPenalty ?? -10,
        ),
        macdBullishScore: Number(value.evaluation?.macdBullishScore ?? 6),
        macdBearishPenalty: Number(value.evaluation?.macdBearishPenalty ?? -8),
        macdHistogramPositiveScore: Number(
          value.evaluation?.macdHistogramPositiveScore ?? 4,
        ),
        macdHistogramNegativePenalty: Number(
          value.evaluation?.macdHistogramNegativePenalty ?? -4,
        ),
        momentumWideBollingerBandwidth: Number(
          value.evaluation?.momentumWideBollingerBandwidth ?? 3,
        ),
        momentumMediumBollingerBandwidth: Number(
          value.evaluation?.momentumMediumBollingerBandwidth ?? 2,
        ),
        momentumNarrowBollingerBandwidth: Number(
          value.evaluation?.momentumNarrowBollingerBandwidth ?? 1,
        ),
        momentumWideBollingerScore: Number(
          value.evaluation?.momentumWideBollingerScore ?? 5,
        ),
        momentumMediumBollingerScore: Number(
          value.evaluation?.momentumMediumBollingerScore ?? 3,
        ),
        momentumNarrowBollingerPenalty: Number(
          value.evaluation?.momentumNarrowBollingerPenalty ?? -8,
        ),
        pullbackWideBollingerBandwidth: Number(
          value.evaluation?.pullbackWideBollingerBandwidth ?? 2,
        ),
        pullbackNarrowBollingerBandwidth: Number(
          value.evaluation?.pullbackNarrowBollingerBandwidth ?? 1,
        ),
        pullbackWideBollingerScore: Number(
          value.evaluation?.pullbackWideBollingerScore ?? 5,
        ),
        pullbackNarrowBollingerPenalty: Number(
          value.evaluation?.pullbackNarrowBollingerPenalty ?? -5,
        ),
        momentumExhaustedMovePenalty: Number(
          value.evaluation?.momentumExhaustedMovePenalty ?? -15,
        ),
        pullbackExhaustedMovePenalty: Number(
          value.evaluation?.pullbackExhaustedMovePenalty ?? -20,
        ),
        superTrendBearishPenalty: Number(
          value.evaluation?.superTrendBearishPenalty ?? -10,
        ),
        highChoppinessPenalty: Number(
          value.evaluation?.highChoppinessPenalty ?? -10,
        ),
      },
      virtualTrading: {
        warmupSeconds: Number(value.virtualTrading?.warmupSeconds ?? 8),
        observationSeconds: Number(
          value.virtualTrading?.observationSeconds ?? 15,
        ),
        maximumObservationSeconds: Number(
          value.virtualTrading?.maximumObservationSeconds ?? 45,
        ),
        tickWindow: Number(value.virtualTrading?.tickWindow ?? 20),
        entryLossPercent: Number(value.virtualTrading?.entryLossPercent ?? 0.2),
        highestPriceTolerance: Number(
          value.virtualTrading?.highestPriceTolerance ?? 0.3,
        ),
        tradeExpirySeconds: Number(
          value.virtualTrading?.tradeExpirySeconds ?? 30,
        ),
        minimumObservationForTrendSeconds: Number(
          value.virtualTrading?.minimumObservationForTrendSeconds ?? 30,
        ),
        confidenceBonusAfterSeconds1: Number(
          value.virtualTrading?.confidenceBonusAfterSeconds1 ?? 20,
        ),
        confidenceBonusAfterSeconds2: Number(
          value.virtualTrading?.confidenceBonusAfterSeconds2 ?? 35,
        ),
        pullbackWarmupSeconds: Number(
          value.virtualTrading?.pullbackWarmupSeconds ?? 8,
        ),
      },
      exit: {
        atrExitMultiplier: Number(value.exit?.atrExitMultiplier ?? 0.4),
        minimumProfitPercent: Number(value.exit?.minimumProfitPercent ?? 0.25),
        trailingActivationNetProfit: Number(
          value.exit?.trailingActivationNetProfit ?? 0,
        ),
        trailingStopAtrMultiplier: Number(
          value.exit?.trailingStopAtrMultiplier ?? 0.6,
        ),
      },
      confidence: {
        maximumGainBonus: Number(value.confidence?.maximumGainBonus ?? 15),
        gainBonusMultiplier: Number(
          value.confidence?.gainBonusMultiplier ?? 20,
        ),
        strongPositiveTickRatio: Number(
          value.confidence?.strongPositiveTickRatio ?? 3,
        ),
        mediumPositiveTickRatio: Number(
          value.confidence?.mediumPositiveTickRatio ?? 2,
        ),
        strongTickBonus: Number(value.confidence?.strongTickBonus ?? 5),
        mediumTickBonus: Number(value.confidence?.mediumTickBonus ?? 3),
        weakTickBonus: Number(value.confidence?.weakTickBonus ?? 1),
        maximumPositiveStreakBonus: Number(
          value.confidence?.maximumPositiveStreakBonus ?? 5,
        ),
        maximumHigherHighBonus: Number(
          value.confidence?.maximumHigherHighBonus ?? 5,
        ),
        drawdownPenaltyMultiplier: Number(
          value.confidence?.drawdownPenaltyMultiplier ?? 5,
        ),
        idealMinimumRSI: Number(value.confidence?.idealMinimumRSI ?? 55),
        idealMaximumRSI: Number(value.confidence?.idealMaximumRSI ?? 65),
        overboughtRSI: Number(value.confidence?.overboughtRSI ?? 70),
        oversoldRSI: Number(value.confidence?.oversoldRSI ?? 50),
        macdBullishBonus: Number(value.confidence?.macdBullishBonus ?? 4),
        macdBearishPenalty: Number(value.confidence?.macdBearishPenalty ?? -6),
        macdHistogramBonus: Number(value.confidence?.macdHistogramBonus ?? 2),
        macdHistogramPenalty: Number(
          value.confidence?.macdHistogramPenalty ?? -2,
        ),
        highVolumeMultiplier: Number(
          value.confidence?.highVolumeMultiplier ?? 3,
        ),
        mediumVolumeMultiplier: Number(
          value.confidence?.mediumVolumeMultiplier ?? 2,
        ),
        lowVolumeMultiplier: Number(
          value.confidence?.lowVolumeMultiplier ?? 1.5,
        ),
        highVolumeBonus: Number(value.confidence?.highVolumeBonus ?? 3),
        mediumVolumeBonus: Number(value.confidence?.mediumVolumeBonus ?? 2),
        lowVolumeBonus: Number(value.confidence?.lowVolumeBonus ?? 1),
        wideBandwidth: Number(value.confidence?.wideBandwidth ?? 3),
        narrowBandwidth: Number(value.confidence?.narrowBandwidth ?? 1),
        wideBandwidthBonus: Number(value.confidence?.wideBandwidthBonus ?? 3),
        narrowBandwidthPenalty: Number(
          value.confidence?.narrowBandwidthPenalty ?? -4,
        ),
        observationBonus1: Number(value.confidence?.observationBonus1 ?? 2),
        observationBonus2: Number(value.confidence?.observationBonus2 ?? 2),
        movementWeight: Number(value.confidence?.movementWeight ?? 0.15),
        trendStrengthWeight: Number(
          value.confidence?.trendStrengthWeight ?? 0.05,
        ),
        trendStabilityWeight: Number(
          value.confidence?.trendStabilityWeight ?? 0.05,
        ),
        recoveryWeight: Number(value.confidence?.recoveryWeight ?? 0.03),
        breakoutWeight: Number(value.confidence?.breakoutWeight ?? 0.02),
      },
      reporting: {
        strongBuyConfidence: Number(value.reporting?.strongBuyConfidence ?? 95),
        buyConfidence: Number(value.reporting?.buyConfidence ?? 90),
        watchConfidence: Number(value.reporting?.watchConfidence ?? 85),
        movementScoreThreshold: Number(
          value.reporting?.movementScoreThreshold ?? 90,
        ),
        trendStrengthThreshold: Number(
          value.reporting?.trendStrengthThreshold ?? 80,
        ),
        trendStabilityThreshold: Number(
          value.reporting?.trendStabilityThreshold ?? 80,
        ),
        recoveryScoreThreshold: Number(
          value.reporting?.recoveryScoreThreshold ?? 80,
        ),
        higherHighCountThreshold: Number(
          value.reporting?.higherHighCountThreshold ?? 5,
        ),
        volumeMultiplierThreshold: Number(
          value.reporting?.volumeMultiplierThreshold ?? 2,
        ),
        maxDrawdownPercentThreshold: Number(
          value.reporting?.maxDrawdownPercentThreshold ?? 0.25,
        ),
        volatilityScoreThreshold: Number(
          value.reporting?.volatilityScoreThreshold ?? 60,
        ),
        riskRewardThreshold: Number(value.reporting?.riskRewardThreshold ?? 2),
      },
    };

    this.#angel
      .saveTradingConfiguration(configuration)

      .pipe(
        finalize(() => {
          this.saving = false;
        }),
      )

      .subscribe({
        next: () => {
          this.#toastService.success(
            'Trading configuration saved successfully.',
          );

          this.form.markAsPristine();
          this.#router.navigate(['/home/dashboard']);
        },

        error: () => {
          this.#toastService.error('Unable to save configuration.');
        },
      });
  }

  // ======================================================
// Download Configuration
// ======================================================

downloadConfiguration(): void {
  const value = this.form.getRawValue();

  const configuration = {
    ...value,

    // Convert UI time values back to backend TimeSpan format.
    marketOpenTime: this.toTimeSpan(value.marketOpenTime),
    marketCloseTime: this.toTimeSpan(value.marketCloseTime),

    // Convert comma-separated UI text back to an array.
    excludedSymbols: this.parseExcludedSymbols(value.excludedSymbolsText),

    // UI-only field is not part of the configuration model.
    excludedSymbolsText: undefined,
  };

  // Remove the UI-only property from the exported JSON.
  delete (configuration as any).excludedSymbolsText;

  const json = JSON.stringify(configuration, null, 2);

  const blob = new Blob([json], {
    type: 'application/json;charset=utf-8',
  });

  const url = URL.createObjectURL(blob);

  const anchor = document.createElement('a');
  anchor.href = url;

  const timestamp = new Date()
    .toISOString()
    .replace(/[:.]/g, '-');

  anchor.download = `trading-configuration-${timestamp}.json`;

  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);

  URL.revokeObjectURL(url);

  this.#toastService.success(
    'Trading configuration downloaded successfully.',
  );
}

  cancel(): void {
    if (this.form.dirty) {
      const confirmed = confirm('Discard unsaved changes?');

      if (!confirmed) {
        return;
      }
    }

    this.#router.navigate(['/home/dashboard']);
  }
}
