import { Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';

import { CommonModule } from '@angular/common';

import {
  AbstractControl,
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';

import { Router } from '@angular/router';
import { catchError, finalize, of } from 'rxjs';
import { ToastService } from '../../../../../services/toast.service';
import {
  TradingConfiguration,
  InstrumentTradingSettings,
  TradingStrictnessProfile,
} from '../../../models/trading-settings';
import { TradingStrategy } from '../../../models/enum/trading-strategy';
import { AngelOneService } from '../../../services/angel-one.service';

@Component({
  selector: 'app-trading-settings',

  standalone: true,

  imports: [CommonModule, ReactiveFormsModule],

  templateUrl: './trading-settings.component.html',

  styleUrl: './trading-settings.component.css',
})
export class TradingSettingsComponent implements OnInit {
  @ViewChild('settingsGrid') settingsGrid?: ElementRef<HTMLElement>;

  readonly #fb = inject(FormBuilder);

  readonly #angel = inject(AngelOneService);

  readonly #toastService = inject(ToastService);

  readonly #router = inject(Router);


  loading = false;

  saving = false;


  readonly tradingStrictnessProfiles: {
    value: TradingStrictnessProfile;
    text: string;
  }[] = [
    { value: 'VeryLoose', text: 'Very Loose — Maximum Exploration' },
    { value: 'Loose', text: 'Loose — Broad Trading' },
    { value: 'Balanced', text: 'Balanced' },
    { value: 'Moderate', text: 'Moderate — More Selective' },
    { value: 'Strict', text: 'Strict — High Confirmation' },
    { value: 'VeryStrict', text: 'Very Strict — Maximum Selectivity' },
  ];

  readonly availableExchanges = [
    { value: 'NSE', text: 'NSE' },
    { value: 'BSE', text: 'BSE' },
  ];

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


  get isEquity(): boolean { return true; }


  get isMomentum(): boolean {
    return Number(this.form?.controls?.strategy?.value) === TradingStrategy.Momentum;
  }

  strategyName(
    strategy: TradingStrategy | number | string | null | undefined,
  ): string {
    if (strategy === null || strategy === undefined || strategy === '') {
      return '—';
    }

    if (typeof strategy === 'string') {
      const normalized = strategy.trim().toLowerCase();

      if (normalized === 'pullback') {
        return 'Pullback';
      }

      if (normalized === 'momentum') {
        return 'Momentum';
      }

      const numeric = Number(normalized);
      if (!Number.isNaN(numeric)) {
        return numeric === TradingStrategy.Pullback ? 'Pullback' : 'Momentum';
      }

      return '—';
    }

    return Number(strategy) === TradingStrategy.Pullback
      ? 'Pullback'
      : 'Momentum';
  }

  get isPullback(): boolean {
    return (
      Number(this.form?.controls?.strategy?.value) === TradingStrategy.Pullback
    );
  }

  form = this.#fb.group({
    tradingStrictnessProfile: ['VeryLoose' as TradingStrictnessProfile],
    enableAutoTrading: [{ value: false, disabled: false }],

    paperTrading: [true],

    enableVirtualTrading: [false],

    enableNotification: [true],

    strategy: [1, Validators.required],


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
    maximumOpenPositions: [3, Validators.required],
    maximumRiskPerUnderlying: [2500, Validators.required],
    maximumPositionsPerUnderlying: [1, Validators.required],
    maximumMarginUtilizationPercent: [70, Validators.required],
    forceSquareOffBufferMinutes: [15, Validators.required],


    riskPercentage: [
      2,

      [Validators.required, Validators.min(0.1), Validators.max(100)],
    ],

    maxCapitalPerTradePercent: [10, [Validators.required, Validators.min(0), Validators.max(100)]],

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

    enableLiveTradingPerformanceGate: [false],
    minimumLiveTradingPerformanceTrades: [10, [Validators.min(0)]],
    minimumLiveTradingWinRate: [55, [Validators.min(0), Validators.max(100)]],
    minimumLiveTradingProfitFactor: [1.2, [Validators.min(0)]],
    minimumLiveTradingNetProfit: [0, [Validators.min(0)]],
    minimumLiveTradingRiskReward: [1.5, [Validators.min(0)]],
    minimumLiveTradingConfidence: [
      60,
      [Validators.min(0), Validators.max(100)],
    ],
    minimumRecentLiveTradingTrades: [5, [Validators.min(0)]],
    requirePositiveRecentLiveTradingNetProfit: [true],
    requireBestStrategyMatchForLiveTrading: [true],
    requireRecentPerformanceToRetainLiveTradingEligibility: [true],
    minimumRecentLiveTradingWinRateToRetainEligibility: [
      40,
      [Validators.min(0), Validators.max(100)],
    ],
    minimumRecentLiveTradingProfitFactorToRetainEligibility: [
      0.9,
      [Validators.min(0)],
    ],

    enablePaperTradingPerformanceGate: [true],
    minimumPaperTradingPerformanceTrades: [3, [Validators.min(0)]],
    minimumPaperTradingWinRate: [45, [Validators.min(0), Validators.max(100)]],
    minimumPaperTradingProfitFactor: [0.8, [Validators.min(0)]],
    minimumPaperTradingNetProfit: [0, [Validators.min(0)]],
    minimumPaperTradingRiskReward: [1.0, [Validators.min(0)]],
    minimumPaperTradingConfidence: [
      45,
      [Validators.min(0), Validators.max(100)],
    ],
    requireBestStrategyMatchForPaperTrading: [false],

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
    enableStochastic: [true],
    stochasticPeriod: [14, [Validators.required, Validators.min(2), Validators.max(100)]],
    stochasticKPeriod: [3, [Validators.required, Validators.min(1), Validators.max(20)]],
    stochasticDPeriod: [3, [Validators.required, Validators.min(1), Validators.max(20)]],
    stochasticOversold: [15, [Validators.min(0), Validators.max(100)]],
    stochasticOverbought: [85, [Validators.min(0), Validators.max(100)]],
    enableAroon: [true],
    aroonPeriod: [25, [Validators.required, Validators.min(2), Validators.max(200)]],
    aroonBullishThreshold: [55, [Validators.min(0), Validators.max(100)]],
    aroonBearishThreshold: [45, [Validators.min(0), Validators.max(100)]],
    enableParabolicSAR: [true],
    parabolicSARStep: [0.02, [Validators.required, Validators.min(0.0001), Validators.max(1)]],
    parabolicSARMaximum: [0.2, [Validators.required, Validators.min(0.0001), Validators.max(1)]],

    maxBrokerFailuresBeforeKillSwitch: [5],
    brokerFailureWindowMinutes: [2],
    intradayEntryCutoffTime: ['15:20'],
    equityMisAutoSquareOffTime: ['15:10'],
    roboAutoSquareOffTime: ['15:05'],
    casTransitionStart: ['15:15'],
    casOrderEntryStart: ['15:20'],
    casMarketOnlyEnd: ['15:25'],
    casLimitOnlyEnd: ['15:30'],
    casRandomCloseSafetyCutoff: ['15:28'],
    casEnd: ['15:40'],
    casPostCloseEnd: ['16:00'],
    casPriceBandPercent: [3],
    maximumTotalOpenRisk: [10000],
    emergencyMarginUtilizationPercent: [85],
    enableTradingKillSwitchPersistence: [true],
    enableGlobalRiskLimits: [true],
    riskReservationSeconds: [10],
    includeUnrealizedPnlInDailyLoss: [true],
    requireClosedHigherTimeframeCandles: [true],
    enablePaperMarginSimulation: [true],
    quoteMaxTokensPerRequest: [50],
    quoteRequestsPerSecond: [1],
    maximumSlippagePercent: [0.5],
    enableEntryExecutionRevalidation: [true],
    entryRevalidationMaxAdverseMovePercent: [0.2],
    entryRevalidationMaxAdverseMoveAtr: [0.25],
    entryRevalidationMaxAgeSeconds: [10],
    enableEntryExecutionAudit: [true],
    entryExecutionAuditPostFillTickCount: [20, [Validators.required, Validators.min(0), Validators.max(20)]],
    rejectDuplicateOrderIntent: [true],
    enableScripConsentForCashOrders: [true],
    orderIntentRecoveryIntervalSeconds: [5],
    orderIntentRecoveryInitialDelaySeconds: [2],
    orderIntentUnknownOrderExpiryMinutes: [2],
    webSocketHeartbeatSeconds: [10],
    webSocketPongTimeoutSeconds: [30],
    webSocketRetryInitialSeconds: [10],
    webSocketRetryMaxSeconds: [60],
    brokerPositionConfirmationDelaySeconds: [1],
    squareOffRetryDelaySeconds: [1],
    stopLossConfirmationSeconds: [2],
    capitalAllocationBaseMultiplier: [0.25],
    capitalAllocationConfidenceMultiplier: [0.75],
    eliteMovementScore: [95],
    strongMovementScore: [90],
    eliteCapitalBonus: [0.1],
    strongCapitalBonus: [0.05],
    maximumObservedDrawdownPercent: [0.25],
    brokerBalanceRefreshSeconds: [30],
    globalMaximumMarginUtilizationPercent: [70, [Validators.min(0), Validators.max(100)]],
    marketTimeZoneId: ['Asia/Kolkata', Validators.required],
    tradingHolidaysText: [''],
    visibleColumnsText: [''],

    coreStrategy: this.#fb.group({
      enabled: [true],
      minimumCompletedCandles: [3],
      minimumRecoveryScore: [40],
      minimumPriceChangePercent: [0],
      minimumBreakoutStrength: [0],
    }),

    dynamicEvaluation: this.#fb.group({
      enabled: [true],
      minimumCandleHistory: [30],
      profileLookbackCandles: [20],
      minimumEntryScore: [42],
      minimumQuoteOnlyEntryScore: [32],
      minimumQuoteOnlySubscriptionScore: [28],
      maximumQuoteOnlyRiskPenalty: [8],
      maximumAdaptiveSubscriptions: [150],
      historicalWarmupCandidates: [100],
      strongTrendThreshold: [68],
      developingThreshold: [28],
      maximumEntryScore: [100],
      unknownStockRiskReward: [1.5],
      minimumRiskReward: [1.25],
      maximumRiskReward: [3.5],
      minimumNetProfit: [5],
      poorStockNetRewardMultiplier: [1],
      goodStockNetRewardMultiplier: [1.35],
      excellentStockNetRewardMultiplier: [1.75],
      normalStopAtrMultiplier: [1.15],
      recoveryStopAtrMultiplier: [1.8],
      maximumStopAtrMultiplier: [2.5],
      maximumStructuralStopAtrDistance: [2.5],
      targetExtensionStepAtr: [0.5],
      maximumTargetExtensionIterations: [4],
      strongPerformanceScore: [70],
      excellentPerformanceScore: [85],
      recoveryScoreThreshold: [60],
      minimumRiskMultiplier: [0.35],
      maximumRiskMultiplier: [1.25],
      trendWeight: [18],
      momentumWeight: [16],
      candleWeight: [14],
      volumeWeight: [10],
      priceActionWeight: [14],
      recoveryWeight: [10],
      regimeWeight: [8],
      multiTimeframeWeight: [8],
      spreadPenaltyWeight: [8],
      exhaustionPenalty: [12],
      minimumExpectedNetValue: [0],
      minimumEdgeScore: [45],
      minimumStatisticalConfidence: [20],
      noTradePenaltyThreshold: [65],
      maximumRiskWhenStatisticallyUncertain: [0.65],
      recentPerformanceWeight: [0.35],
      historicalPerformanceWeight: [0.65],
      marketRegimeWeight: [0.10],
      relativeStrengthWeight: [0.10],
    }),

    dynamicVirtualTrading: this.#fb.group({
      enabled: [true],
      minimumObservationTicks: [3],
      maximumObservationTicks: [40],
      minimumObservationSeconds: [1],
      maximumObservationSeconds: [30],
      minimumFavorableTickRatio: [0.52],
      maximumAdverseTickRatio: [0.6],
      recoveryTickRatioBonus: [0.08],
      maximumAdverseMoveAtr: [0.9],
      recoveryMaximumAdverseMoveAtr: [1.5],
      minimumTickMomentum: [0.05],
      minimumTrendStability: [45],
      minimumMovementScore: [35],
      maximumNoiseScoreForEntry: [75],
      minimumRecoveryScore: [45],
      minimumBreakoutStrength: [35],
      minimumPriceSlope: [-0.05],
      minimumProfitAtrBeforeTrailing: [0.5],
      baseTrailingAtrMultiplier: [1.2],
      strongTrendTrailingAtrMultiplier: [1.5],
      recoveryTrailingAtrMultiplier: [1.8],
      weakTrendTrailingAtrMultiplier: [0.9],
      minimumTrailingAtrMultiplier: [0.7],
      maximumTrailingAtrMultiplier: [2.2],
      minimumExecutionConfidence: [35],
      peakProfitRetentionPercent: [60],
      recoveryPeakProfitRetentionPercent: [40],
      tickPriceMoveWeight: [30],
      tickDirectionWeight: [25],
      tickAccelerationWeight: [15],
      tickVolumeWeight: [10],
      tickSpreadWeight: [10],
      tickRecoveryWeight: [10],
      baseCapitalMultiplier: [0.35],
      confidenceCapitalMultiplier: [0.65],
    }),

    futureTickPrediction: this.#fb.group({
      enabled: [true],
      shadowMode: [false],
      horizonTicks: [5, [Validators.required, Validators.min(1)]],
      horizonSeconds: [10, [Validators.required, Validators.min(0)]],
      minimumFavorableProbability: [0.48, [Validators.min(0), Validators.max(1)]],
      useAdaptiveEntryGate: [true],
      strongStrategyScore: [70, [Validators.min(0), Validators.max(100)]],
      adaptiveMinimumFavorableProbability: [0.42, [Validators.min(0), Validators.max(1)]],
      adaptiveMaximumAdverseProbability: [0.50, [Validators.min(0), Validators.max(1)]],
      adaptiveMinimumContinuationScore: [0.02],
      adaptiveMinimumPredictionConfidence: [0.30, [Validators.min(0), Validators.max(1)]],
      maximumAdverseProbability: [0.48, [Validators.min(0), Validators.max(1)]],
      minimumContinuationScore: [0.08],
      minimumPredictionConfidence: [0.40, [Validators.min(0), Validators.max(1)]],
      minimumExpectedNetValue: [-0.25],
      maximumAdverseMoveAtr: [0.65, [Validators.min(0)]],
      minimumVolumeParticipation: [0.25, [Validators.min(0), Validators.max(1)]],
      immediateFailureScore: [35, [Validators.min(0), Validators.max(100)]],
      minimumObservedTicksForPostEntryGate: [3, [Validators.min(0)]],
      consecutiveAdverseTicksForEarlyExit: [3, [Validators.min(0)]],
      shadowStorage: this.#fb.group({
        enabled: [true],
        path: ['TradeAnalysis/FutureTickPrediction'],
        minimumRecordIntervalSecondsPerSymbol: [1, [Validators.min(0)]],
        retentionDays: [90, [Validators.min(0)]],
      }),
    }),

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
      momentumMaximumDrawdown: [
        1,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      momentumHighestPriceTolerance: [
        0.997,
        [Validators.min(0), Validators.max(1)],
      ],
      pullbackMinimumPriceRatio: [
        0.998,
        [Validators.min(0), Validators.max(1)],
      ],
      maximumPullbackGain: [
        1,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
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
      ema9BelowEma21Score: [10],
      priceBelowVwapScore: [5],
      superTrendBearishScore: [5],
      minusDiAbovePlusDiScore: [5],
      lastCandleBearishScore: [5],
      lowerLowScore: [3],
      momentumStochasticBullishScore: [3],
      momentumStochasticBearishScore: [0],
      pullbackStochasticBullishScore: [3],
      pullbackStochasticBearishScore: [0],
      momentumAroonBullishScore: [3],
      momentumAroonBearishScore: [0],
      pullbackAroonBullishScore: [3],
      pullbackAroonBearishScore: [0],
      momentumParabolicSARBullishScore: [3],
      momentumParabolicSARBearishScore: [0],
      pullbackParabolicSARBullishScore: [3],
      pullbackParabolicSARBearishScore: [0],
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
      entryMinimumPriceRatio: [0],
      maximumDrawdownPercent: [100],
      highestPriceMinimumRatio: [0],
      positiveRatioWeight: [40],
      aboveEntryRatioWeight: [30],
      maximumHigherHighBonus: [15],
      maximumConsecutivePositiveBonus: [15],
      volatilityVeryLowThreshold: [0.2],
      volatilityLowThreshold: [0.5],
      volatilityMediumThreshold: [0.8],
      volatilityHighThreshold: [1.2],
      volatilityVeryHighThreshold: [2],
      fallbackAtrPercent: [0.5],
      maximumPullbackGainPercent: [100],
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
      enableVirtualTradeTickEmails: [true],
      minimumVirtualTradeTicksForEmail: [3, [Validators.min(1)]],
      virtualTradeEmailStages: this.#fb.group({
        expired: [true],
        rejected: [true],
        exit: [true],
      }),
      exitPostSellTickCount: [3, [Validators.min(0), Validators.max(100)]],
      tradeAnalyticsPersistenceIntervalSeconds: [5, [Validators.min(0), Validators.max(300)]],
      tradeAnalysisPublicBaseUrl: [''],
    }),

    allStockAnalysisArchive: this.#fb.group({
      enabled: [false],
      path: ['TradeAnalysis/AllStocks'],
      samplingIntervalSecondsPerSymbol: [15, [Validators.min(1), Validators.max(300)]],
      flushIntervalSeconds: [15, [Validators.min(1), Validators.max(300)]],
      maximumBufferedRecords: [5000, [Validators.min(100), Validators.max(100000)]],
      chunkMinutes: [15, [Validators.min(1), Validators.max(60)]],
      retentionDays: [30, [Validators.min(1), Validators.max(3650)]],
      recordStateChanges: [false],
      includeConfigurationSnapshot: [true],
      recordLoadingCandidates: [true],
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
      trailingProfitRetentionPercent: [
        70,
        [Validators.required, Validators.min(0), Validators.max(100)],
      ],
      enableTradeFailureExit: [true],
      tradeFailureMinimumHoldingSeconds: [30, [Validators.required, Validators.min(0)]],
      tradeFailureMinimumFavorableExcursionPercent: [0.25, [Validators.required, Validators.min(0)]],
      tradeFailureMaximumAdverseExcursionPercent: [1, [Validators.required, Validators.min(0)]],
      tradeFailureMaximumAdverseAtrMultiplier: [1.5, [Validators.required, Validators.min(0)]],
      tradeFailureHealthScoreThreshold: [45, [Validators.required, Validators.min(0), Validators.max(100)]],
      tradeFailureRequireTrendDeterioration: [true],
      tradeFailureRequireMomentumDeterioration: [true],
    }),
  });

  enableAutoTradingFormControl = this.form?.controls?.enableAutoTrading;
  enableAutoTradingPreviousValue = this.enableAutoTradingFormControl?.value;

  settingsSearch = '';

  filterSettings(event: Event): void {
    this.settingsSearch = (event.target as HTMLInputElement).value;
    const query = this.settingsSearch.trim().toLowerCase();
    const cards = this.settingsGrid?.nativeElement.querySelectorAll<HTMLDetailsElement>(
      'details.settings-card',
    );

    cards?.forEach((card) => {
      const matches = !query || card.textContent?.toLowerCase().includes(query);
      card.hidden = !matches;

      if (query && matches) {
        card.open = true;
      }
    });
  }

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

    this.form.controls.strategy.valueChanges.subscribe(() => {
      // Strategy visibility is derived from the current form value. Mark the form dirty
      // because changing strategy changes which strategy-specific settings are active.
      this.form.markAsDirty();
    });

    this.form.controls.tradingStrictnessProfile.valueChanges.subscribe(
      (profile) => {
        if (profile) {
          this.applyTradingStrictnessProfile(
            profile as TradingStrictnessProfile,
          );
          this.form.markAsDirty();
        }
      },
    );

    this.loadConfiguration();
  }

  // ======================================================
  // Load Configuration
  // ======================================================

  private applyTradingStrictnessProfile(
    profile: TradingStrictnessProfile,
  ): void {
    const presets: Record<TradingStrictnessProfile, any> = {
      VeryLoose: {
        evaluation: {
          strongAdx: 10,
          mediumAdx: 8,
          lowChoppiness: 20,
          highChoppiness: 80,
          highRelativeVolume: 0.5,
          mediumRelativeVolume: 0.4,
          lowRelativeVolume: 0.3,
          excellentScore: 50,
          goodScore: 40,
          averageScore: 30,
        },
        virtualTrading: {
          warmupSeconds: 0,
          observationSeconds: 3,
          maximumObservationSeconds: 10,
          tickWindow: 5,
          entryLossPercent: 1,
          highestPriceTolerance: 1,
          tradeExpirySeconds: 15,
          minimumObservationForTrendSeconds: 0,
          confidenceBonusAfterSeconds1: 0,
          confidenceBonusAfterSeconds2: 0,
          pullbackWarmupSeconds: 0,
        },
      validation: {
          minimumMovementScore: 10,
          minimumConfidence: 20,
          minimumRiskReward: 0.1,
          minimumTrendStrength: 10,
          minimumTrendStability: 10,
          minimumRecoveryScore: 10,
          minimumVolatilityScore: 5,
          minimumNoiseScore: 5,
          minimumBreakoutStrength: 5,
          minimumRSI: 0,
          maximumRSI: 100,
          minimumVolumeMultiplier: 0,
          minimumPositiveTickRatio: 0,
          minimumAboveEntryRatio: 0,
          minimumHigherHighs: 0,
          minimumConsecutivePositiveTicks: 0,
          maximumConsecutiveNegativeTicks: 100,
          maximumDrawdownPercent: 5,
          minimumFinalScore: 20,
          minimumBollingerBandwidth: 0,
          minimumFinalRSI: 0,
          maximumFinalRSI: 100,
        },
      },
      Loose: {
        evaluation: {
          strongAdx: 15,
          mediumAdx: 12,
          lowChoppiness: 25,
          highChoppiness: 75,
          highRelativeVolume: 0.8,
          mediumRelativeVolume: 0.6,
          lowRelativeVolume: 0.5,
          excellentScore: 60,
          goodScore: 50,
          averageScore: 40,
        },
        virtualTrading: {
          warmupSeconds: 1,
          observationSeconds: 5,
          maximumObservationSeconds: 15,
          tickWindow: 8,
          entryLossPercent: 0.75,
          highestPriceTolerance: 0.75,
          tradeExpirySeconds: 20,
          minimumObservationForTrendSeconds: 3,
          confidenceBonusAfterSeconds1: 5,
          confidenceBonusAfterSeconds2: 10,
          pullbackWarmupSeconds: 1,
        },
      },
      Balanced: {
        evaluation: {
          strongAdx: 30,
          mediumAdx: 25,
          lowChoppiness: 38,
          highChoppiness: 55,
          highRelativeVolume: 2,
          mediumRelativeVolume: 1.5,
          lowRelativeVolume: 1.2,
          excellentScore: 90,
          goodScore: 80,
          averageScore: 70,
        },
        virtualTrading: {
          warmupSeconds: 8,
          observationSeconds: 15,
          maximumObservationSeconds: 45,
          tickWindow: 20,
          entryLossPercent: 0.2,
          highestPriceTolerance: 0.3,
          tradeExpirySeconds: 30,
          minimumObservationForTrendSeconds: 30,
          confidenceBonusAfterSeconds1: 20,
          confidenceBonusAfterSeconds2: 35,
          pullbackWarmupSeconds: 8,
        },
      },
      Moderate: {
        evaluation: {
          strongAdx: 35,
          mediumAdx: 30,
          lowChoppiness: 35,
          highChoppiness: 50,
          highRelativeVolume: 2.5,
          mediumRelativeVolume: 2,
          lowRelativeVolume: 1.5,
          excellentScore: 92,
          goodScore: 85,
          averageScore: 75,
        },
        virtualTrading: {
          warmupSeconds: 10,
          observationSeconds: 20,
          maximumObservationSeconds: 50,
          tickWindow: 25,
          entryLossPercent: 0.15,
          highestPriceTolerance: 0.2,
          tradeExpirySeconds: 40,
          minimumObservationForTrendSeconds: 35,
          confidenceBonusAfterSeconds1: 25,
          confidenceBonusAfterSeconds2: 45,
          pullbackWarmupSeconds: 10,
        },
      },
      Strict: {
        evaluation: {
          strongAdx: 40,
          mediumAdx: 35,
          lowChoppiness: 32,
          highChoppiness: 45,
          highRelativeVolume: 3,
          mediumRelativeVolume: 2.5,
          lowRelativeVolume: 2,
          excellentScore: 95,
          goodScore: 90,
          averageScore: 80,
        },
        virtualTrading: {
          warmupSeconds: 12,
          observationSeconds: 25,
          maximumObservationSeconds: 60,
          tickWindow: 30,
          entryLossPercent: 0.1,
          highestPriceTolerance: 0.15,
          tradeExpirySeconds: 45,
          minimumObservationForTrendSeconds: 40,
          confidenceBonusAfterSeconds1: 30,
          confidenceBonusAfterSeconds2: 50,
          pullbackWarmupSeconds: 12,
        },
      },
      VeryStrict: {
        evaluation: {
          strongAdx: 45,
          mediumAdx: 40,
          lowChoppiness: 30,
          highChoppiness: 40,
          highRelativeVolume: 4,
          mediumRelativeVolume: 3,
          lowRelativeVolume: 2.5,
          excellentScore: 98,
          goodScore: 95,
          averageScore: 90,
        },
        virtualTrading: {
          warmupSeconds: 15,
          observationSeconds: 30,
          maximumObservationSeconds: 75,
          tickWindow: 40,
          entryLossPercent: 0.05,
          highestPriceTolerance: 0.1,
          tradeExpirySeconds: 60,
          minimumObservationForTrendSeconds: 45,
          confidenceBonusAfterSeconds1: 40,
          confidenceBonusAfterSeconds2: 60,
          pullbackWarmupSeconds: 15,
        },
      },
    };
    const preset = presets[profile] ?? presets.VeryLoose;
    this.form.controls.evaluation.patchValue(preset.evaluation, {
      emitEvent: false,
    });
    this.form.controls.virtualTrading.patchValue(preset.virtualTrading, {
      emitEvent: false,
    });
    if (preset.validation)
      this.form.controls.validation.patchValue(preset.validation, {
        emitEvent: false,
      });
  }

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
    const patch = this.removeNullishValues({

        enableAutoTrading: configuration.enableAutoTrading,

        paperTrading: configuration.paperTrading,

        enableVirtualTrading: configuration.enableVirtualTrading ?? false,

        enableNotification: configuration.enableNotification,

        strategy: this.normalizeStrategy(configuration.strategy),

        tradingStrictnessProfile:
          configuration.tradingStrictnessProfile ?? 'VeryLoose',

        riskPercentage: configuration.riskPercentage,

        maxCapitalPerTradePercent: configuration.maxCapitalPerTradePercent,

        maxDailyLoss: configuration.maxDailyLoss,

        maxDailyTrades: configuration.maxDailyTrades,

        cooldownMinutes: configuration.cooldownMinutes,

        ignoreMarketHours: configuration.ignoreMarketHours,

        marketOpenTime: this.toTimeInput(configuration.marketOpenTime),

        marketCloseTime: this.toTimeInput(configuration.marketCloseTime),

        maxBrokerFailuresBeforeKillSwitch: configuration.maxBrokerFailuresBeforeKillSwitch ?? 5,
        brokerFailureWindowMinutes: configuration.brokerFailureWindowMinutes ?? 2,
        intradayEntryCutoffTime: this.toTimeInput(configuration.intradayEntryCutoffTime),
        equityMisAutoSquareOffTime: this.toTimeInput(configuration.equityMisAutoSquareOffTime),
        roboAutoSquareOffTime: this.toTimeInput(configuration.roboAutoSquareOffTime),
        casTransitionStart: this.toTimeInput(configuration.casTransitionStart),
        casOrderEntryStart: this.toTimeInput(configuration.casOrderEntryStart),
        casMarketOnlyEnd: this.toTimeInput(configuration.casMarketOnlyEnd),
        casLimitOnlyEnd: this.toTimeInput(configuration.casLimitOnlyEnd),
        casRandomCloseSafetyCutoff: configuration.casRandomCloseSafetyCutoff ?? '15:28',
        casEnd: this.toTimeInput(configuration.casEnd),
        casPostCloseEnd: this.toTimeInput(configuration.casPostCloseEnd),
        casPriceBandPercent: configuration.casPriceBandPercent ?? 3,
        maximumTotalOpenRisk: configuration.maximumTotalOpenRisk ?? 10000,
        maximumMarginUtilizationPercent: configuration.maximumMarginUtilizationPercent ?? 70,
        emergencyMarginUtilizationPercent: configuration.emergencyMarginUtilizationPercent ?? 85,
        enableTradingKillSwitchPersistence: configuration.enableTradingKillSwitchPersistence ?? true,
        enableGlobalRiskLimits: configuration.enableGlobalRiskLimits ?? true,
        riskReservationSeconds: configuration.riskReservationSeconds ?? 10,
        includeUnrealizedPnlInDailyLoss: configuration.includeUnrealizedPnlInDailyLoss ?? true,
        requireClosedHigherTimeframeCandles: configuration.requireClosedHigherTimeframeCandles ?? true,
        enablePaperMarginSimulation: configuration.enablePaperMarginSimulation ?? true,
        quoteMaxTokensPerRequest: configuration.quoteMaxTokensPerRequest ?? 50,
        quoteRequestsPerSecond: configuration.quoteRequestsPerSecond ?? 1,
        maximumSlippagePercent: configuration.maximumSlippagePercent ?? 0.5,
        rejectDuplicateOrderIntent: configuration.rejectDuplicateOrderIntent ?? true,
        enableScripConsentForCashOrders: configuration.enableScripConsentForCashOrders ?? true,
        orderIntentRecoveryIntervalSeconds: configuration.orderIntentRecoveryIntervalSeconds ?? 5,
        orderIntentRecoveryInitialDelaySeconds: configuration.orderIntentRecoveryInitialDelaySeconds ?? 2,
        orderIntentUnknownOrderExpiryMinutes: configuration.orderIntentUnknownOrderExpiryMinutes ?? 2,
        webSocketHeartbeatSeconds: configuration.webSocketHeartbeatSeconds ?? 10,
        webSocketPongTimeoutSeconds: configuration.webSocketPongTimeoutSeconds ?? 30,
        webSocketRetryInitialSeconds: configuration.webSocketRetryInitialSeconds ?? 10,
        webSocketRetryMaxSeconds: configuration.webSocketRetryMaxSeconds ?? 60,
        brokerPositionConfirmationDelaySeconds: configuration.brokerPositionConfirmationDelaySeconds ?? 1,
        squareOffRetryDelaySeconds: configuration.squareOffRetryDelaySeconds ?? 1,
        stopLossConfirmationSeconds: configuration.stopLossConfirmationSeconds ?? 2,
        capitalAllocationBaseMultiplier: configuration.capitalAllocationBaseMultiplier ?? 0.25,
        capitalAllocationConfidenceMultiplier: configuration.capitalAllocationConfidenceMultiplier ?? 0.75,
        eliteMovementScore: configuration.eliteMovementScore ?? 95,
        strongMovementScore: configuration.strongMovementScore ?? 90,
        eliteCapitalBonus: configuration.eliteCapitalBonus ?? 0.1,
        strongCapitalBonus: configuration.strongCapitalBonus ?? 0.05,
        maximumObservedDrawdownPercent: configuration.maximumObservedDrawdownPercent ?? 0.25,
        brokerBalanceRefreshSeconds: configuration.brokerBalanceRefreshSeconds ?? 30,
        globalMaximumMarginUtilizationPercent: configuration.maximumMarginUtilizationPercent ?? 70,
        marketTimeZoneId: configuration.marketTimeZoneId ?? 'Asia/Kolkata',
        tradingHolidaysText: (configuration.tradingHolidays ?? []).join(', '),
        visibleColumnsText: (configuration.visibleColumns ?? []).join(', '),

        excludedSymbolsText: (configuration.excludedSymbols ?? []).join(', '),

        watchListRefreshMinutes: configuration.watchListRefreshMinutes,

        minPrice: configuration.minPrice,

        minVolume: configuration.minVolume,

        maxCandidates: configuration.maxCandidates,

        minimumRoiPercent: configuration.minimumRoiPercent,

        minimumNetProfit: configuration.minimumNetProfit,

        enableLiveTradingPerformanceGate:
          configuration.enableLiveTradingPerformanceGate ?? false,
        minimumLiveTradingPerformanceTrades:
          configuration.minimumLiveTradingPerformanceTrades ?? 10,
        minimumLiveTradingWinRate:
          configuration.minimumLiveTradingWinRate ?? 55,
        minimumLiveTradingProfitFactor:
          configuration.minimumLiveTradingProfitFactor ?? 1.2,
        minimumLiveTradingNetProfit:
          configuration.minimumLiveTradingNetProfit ?? 0,
        minimumLiveTradingRiskReward:
          configuration.minimumLiveTradingRiskReward ?? 1.5,
        minimumLiveTradingConfidence:
          configuration.minimumLiveTradingConfidence ?? 60,
        minimumRecentLiveTradingTrades:
          configuration.minimumRecentLiveTradingTrades ?? 5,
        requirePositiveRecentLiveTradingNetProfit:
          configuration.requirePositiveRecentLiveTradingNetProfit ?? true,
        requireBestStrategyMatchForLiveTrading:
          configuration.requireBestStrategyMatchForLiveTrading ?? true,
        requireRecentPerformanceToRetainLiveTradingEligibility:
          configuration.requireRecentPerformanceToRetainLiveTradingEligibility ??
          true,
        minimumRecentLiveTradingWinRateToRetainEligibility:
          configuration.minimumRecentLiveTradingWinRateToRetainEligibility ??
          40,
        minimumRecentLiveTradingProfitFactorToRetainEligibility:
          configuration.minimumRecentLiveTradingProfitFactorToRetainEligibility ??
          0.9,
        enablePaperTradingPerformanceGate:
          configuration.enablePaperTradingPerformanceGate ?? true,
        minimumPaperTradingPerformanceTrades:
          configuration.minimumPaperTradingPerformanceTrades ?? 3,
        minimumPaperTradingWinRate:
          configuration.minimumPaperTradingWinRate ?? 45,
        minimumPaperTradingProfitFactor:
          configuration.minimumPaperTradingProfitFactor ?? 0.8,
        minimumPaperTradingNetProfit:
          configuration.minimumPaperTradingNetProfit ?? 0,
        minimumPaperTradingRiskReward:
          configuration.minimumPaperTradingRiskReward ?? 1,
        minimumPaperTradingConfidence:
          configuration.minimumPaperTradingConfidence ?? 45,
        requireBestStrategyMatchForPaperTrading:
          configuration.requireBestStrategyMatchForPaperTrading ?? false,

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
        enableStochastic: configuration.enableStochastic ?? true,
        stochasticPeriod: configuration.stochasticPeriod ?? 14,
        stochasticKPeriod: configuration.stochasticKPeriod ?? 3,
        stochasticDPeriod: configuration.stochasticDPeriod ?? 3,
        stochasticOversold: configuration.stochasticOversold ?? 15,
        stochasticOverbought: configuration.stochasticOverbought ?? 85,
        enableAroon: configuration.enableAroon ?? true,
        aroonPeriod: configuration.aroonPeriod ?? 25,
        aroonBullishThreshold: configuration.aroonBullishThreshold ?? 55,
        aroonBearishThreshold: configuration.aroonBearishThreshold ?? 45,
        enableParabolicSAR: configuration.enableParabolicSAR ?? true,
        parabolicSARStep: configuration.parabolicSARStep ?? 0.02,
        parabolicSARMaximum: configuration.parabolicSARMaximum ?? 0.2,

        coreStrategy: {
          enabled: configuration.coreStrategy?.enabled ?? true,
          minimumCompletedCandles: configuration.coreStrategy?.minimumCompletedCandles ?? 3,
          minimumRecoveryScore: configuration.coreStrategy?.minimumRecoveryScore ?? 40,
          minimumPriceChangePercent: configuration.coreStrategy?.minimumPriceChangePercent ?? 0,
          minimumBreakoutStrength: configuration.coreStrategy?.minimumBreakoutStrength ?? 0,
        },

        dynamicEvaluation: {
          enabled: configuration.dynamicEvaluation?.enabled ?? true,
          minimumCandleHistory: configuration.dynamicEvaluation?.minimumCandleHistory ?? 30,
          profileLookbackCandles: configuration.dynamicEvaluation?.profileLookbackCandles ?? 20,
          minimumEntryScore: configuration.dynamicEvaluation?.minimumEntryScore ?? 42,
          minimumQuoteOnlyEntryScore: configuration.dynamicEvaluation?.minimumQuoteOnlyEntryScore ?? 32,
          minimumQuoteOnlySubscriptionScore: configuration.dynamicEvaluation?.minimumQuoteOnlySubscriptionScore ?? 28,
          maximumQuoteOnlyRiskPenalty: configuration.dynamicEvaluation?.maximumQuoteOnlyRiskPenalty ?? 8,
          maximumAdaptiveSubscriptions: configuration.dynamicEvaluation?.maximumAdaptiveSubscriptions ?? 150,
          historicalWarmupCandidates: configuration.dynamicEvaluation?.historicalWarmupCandidates ?? 100,
          strongTrendThreshold: configuration.dynamicEvaluation?.strongTrendThreshold ?? 68,
          developingThreshold: configuration.dynamicEvaluation?.developingThreshold ?? 28,
          maximumEntryScore: configuration.dynamicEvaluation?.maximumEntryScore ?? 100,
          unknownStockRiskReward: configuration.dynamicEvaluation?.unknownStockRiskReward ?? 1.5,
          minimumRiskReward: configuration.dynamicEvaluation?.minimumRiskReward ?? 1.25,
          maximumRiskReward: configuration.dynamicEvaluation?.maximumRiskReward ?? 3.5,
          minimumNetProfit: configuration.dynamicEvaluation?.minimumNetProfit ?? 5,
          poorStockNetRewardMultiplier: configuration.dynamicEvaluation?.poorStockNetRewardMultiplier ?? 1,
          goodStockNetRewardMultiplier: configuration.dynamicEvaluation?.goodStockNetRewardMultiplier ?? 1.35,
          excellentStockNetRewardMultiplier: configuration.dynamicEvaluation?.excellentStockNetRewardMultiplier ?? 1.75,
          normalStopAtrMultiplier: configuration.dynamicEvaluation?.normalStopAtrMultiplier ?? 1.15,
          recoveryStopAtrMultiplier: configuration.dynamicEvaluation?.recoveryStopAtrMultiplier ?? 1.8,
          maximumStopAtrMultiplier: configuration.dynamicEvaluation?.maximumStopAtrMultiplier ?? 2.5,
          maximumStructuralStopAtrDistance: configuration.dynamicEvaluation?.maximumStructuralStopAtrDistance ?? 2.5,
          targetExtensionStepAtr: configuration.dynamicEvaluation?.targetExtensionStepAtr ?? 0.5,
          maximumTargetExtensionIterations: configuration.dynamicEvaluation?.maximumTargetExtensionIterations ?? 4,
          strongPerformanceScore: configuration.dynamicEvaluation?.strongPerformanceScore ?? 70,
          excellentPerformanceScore: configuration.dynamicEvaluation?.excellentPerformanceScore ?? 85,
          recoveryScoreThreshold: configuration.dynamicEvaluation?.recoveryScoreThreshold ?? 60,
          minimumRiskMultiplier: configuration.dynamicEvaluation?.minimumRiskMultiplier ?? 0.35,
          maximumRiskMultiplier: configuration.dynamicEvaluation?.maximumRiskMultiplier ?? 1.25,
          trendWeight: configuration.dynamicEvaluation?.trendWeight ?? 18,
          momentumWeight: configuration.dynamicEvaluation?.momentumWeight ?? 16,
          candleWeight: configuration.dynamicEvaluation?.candleWeight ?? 14,
          volumeWeight: configuration.dynamicEvaluation?.volumeWeight ?? 10,
          priceActionWeight: configuration.dynamicEvaluation?.priceActionWeight ?? 14,
          recoveryWeight: configuration.dynamicEvaluation?.recoveryWeight ?? 10,
          regimeWeight: configuration.dynamicEvaluation?.regimeWeight ?? 8,
          multiTimeframeWeight: configuration.dynamicEvaluation?.multiTimeframeWeight ?? 8,
          spreadPenaltyWeight: configuration.dynamicEvaluation?.spreadPenaltyWeight ?? 8,
          exhaustionPenalty: configuration.dynamicEvaluation?.exhaustionPenalty ?? 12,
        },

        dynamicVirtualTrading: {
          enabled: configuration.dynamicVirtualTrading?.enabled ?? true,
          minimumObservationTicks: configuration.dynamicVirtualTrading?.minimumObservationTicks ?? 3,
          maximumObservationTicks: configuration.dynamicVirtualTrading?.maximumObservationTicks ?? 40,
          minimumObservationSeconds: configuration.dynamicVirtualTrading?.minimumObservationSeconds ?? 1,
          maximumObservationSeconds: configuration.dynamicVirtualTrading?.maximumObservationSeconds ?? 30,
          minimumFavorableTickRatio: configuration.dynamicVirtualTrading?.minimumFavorableTickRatio ?? 0.52,
          maximumAdverseTickRatio: configuration.dynamicVirtualTrading?.maximumAdverseTickRatio ?? 0.6,
          recoveryTickRatioBonus: configuration.dynamicVirtualTrading?.recoveryTickRatioBonus ?? 0.08,
          maximumAdverseMoveAtr: configuration.dynamicVirtualTrading?.maximumAdverseMoveAtr ?? 0.9,
          recoveryMaximumAdverseMoveAtr: configuration.dynamicVirtualTrading?.recoveryMaximumAdverseMoveAtr ?? 1.5,
          minimumTickMomentum: configuration.dynamicVirtualTrading?.minimumTickMomentum ?? 0.05,
          minimumTrendStability: configuration.dynamicVirtualTrading?.minimumTrendStability ?? 45,
          minimumMovementScore: configuration.dynamicVirtualTrading?.minimumMovementScore ?? 35,
          maximumNoiseScoreForEntry: configuration.dynamicVirtualTrading?.maximumNoiseScoreForEntry ?? 75,
          minimumRecoveryScore: configuration.dynamicVirtualTrading?.minimumRecoveryScore ?? 45,
          minimumBreakoutStrength: configuration.dynamicVirtualTrading?.minimumBreakoutStrength ?? 35,
          minimumPriceSlope: configuration.dynamicVirtualTrading?.minimumPriceSlope ?? -0.05,
          minimumProfitAtrBeforeTrailing: configuration.dynamicVirtualTrading?.minimumProfitAtrBeforeTrailing ?? 0.5,
          baseTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.baseTrailingAtrMultiplier ?? 1.2,
          strongTrendTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.strongTrendTrailingAtrMultiplier ?? 1.5,
          recoveryTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.recoveryTrailingAtrMultiplier ?? 1.8,
          weakTrendTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.weakTrendTrailingAtrMultiplier ?? 0.9,
          minimumTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.minimumTrailingAtrMultiplier ?? 0.7,
          maximumTrailingAtrMultiplier: configuration.dynamicVirtualTrading?.maximumTrailingAtrMultiplier ?? 2.2,
          minimumExecutionConfidence: configuration.dynamicVirtualTrading?.minimumExecutionConfidence ?? 35,
          peakProfitRetentionPercent: configuration.dynamicVirtualTrading?.peakProfitRetentionPercent ?? 60,
          recoveryPeakProfitRetentionPercent: configuration.dynamicVirtualTrading?.recoveryPeakProfitRetentionPercent ?? 40,
          tickPriceMoveWeight: configuration.dynamicVirtualTrading?.tickPriceMoveWeight ?? 30,
          tickDirectionWeight: configuration.dynamicVirtualTrading?.tickDirectionWeight ?? 25,
          tickAccelerationWeight: configuration.dynamicVirtualTrading?.tickAccelerationWeight ?? 15,
          tickVolumeWeight: configuration.dynamicVirtualTrading?.tickVolumeWeight ?? 10,
          tickSpreadWeight: configuration.dynamicVirtualTrading?.tickSpreadWeight ?? 10,
          tickRecoveryWeight: configuration.dynamicVirtualTrading?.tickRecoveryWeight ?? 10,
          baseCapitalMultiplier: configuration.dynamicVirtualTrading?.baseCapitalMultiplier ?? 0.35,
          confidenceCapitalMultiplier: configuration.dynamicVirtualTrading?.confidenceCapitalMultiplier ?? 0.65,
        },

        futureTickPrediction: {
          enabled: configuration.futureTickPrediction?.enabled ?? true,
          shadowMode: configuration.futureTickPrediction?.shadowMode ?? false,
          horizonTicks: configuration.futureTickPrediction?.horizonTicks ?? 5,
          horizonSeconds: configuration.futureTickPrediction?.horizonSeconds ?? 10,
          minimumFavorableProbability: configuration.futureTickPrediction?.minimumFavorableProbability ?? 0.48,
          useAdaptiveEntryGate: configuration.futureTickPrediction?.useAdaptiveEntryGate ?? true,
          strongStrategyScore: configuration.futureTickPrediction?.strongStrategyScore ?? 70,
          adaptiveMinimumFavorableProbability: configuration.futureTickPrediction?.adaptiveMinimumFavorableProbability ?? 0.42,
          adaptiveMaximumAdverseProbability: configuration.futureTickPrediction?.adaptiveMaximumAdverseProbability ?? 0.50,
          adaptiveMinimumContinuationScore: configuration.futureTickPrediction?.adaptiveMinimumContinuationScore ?? 0.02,
          adaptiveMinimumPredictionConfidence: configuration.futureTickPrediction?.adaptiveMinimumPredictionConfidence ?? 0.30,
          maximumAdverseProbability: configuration.futureTickPrediction?.maximumAdverseProbability ?? 0.48,
          minimumContinuationScore: configuration.futureTickPrediction?.minimumContinuationScore ?? 0.08,
          minimumPredictionConfidence: configuration.futureTickPrediction?.minimumPredictionConfidence ?? 0.40,
          minimumExpectedNetValue: configuration.futureTickPrediction?.minimumExpectedNetValue ?? -0.25,
          maximumAdverseMoveAtr: configuration.futureTickPrediction?.maximumAdverseMoveAtr ?? 0.65,
          minimumVolumeParticipation: configuration.futureTickPrediction?.minimumVolumeParticipation ?? 0.25,
          immediateFailureScore: configuration.futureTickPrediction?.immediateFailureScore ?? 35,
          minimumObservedTicksForPostEntryGate: configuration.futureTickPrediction?.minimumObservedTicksForPostEntryGate ?? 3,
          consecutiveAdverseTicksForEarlyExit: configuration.futureTickPrediction?.consecutiveAdverseTicksForEarlyExit ?? 3,
          shadowStorage: {
            enabled: configuration.futureTickPrediction?.shadowStorage?.enabled ?? true,
            path: configuration.futureTickPrediction?.shadowStorage?.path ?? 'TradeAnalysis/FutureTickPrediction',
            minimumRecordIntervalSecondsPerSymbol:
              configuration.futureTickPrediction?.shadowStorage?.minimumRecordIntervalSecondsPerSymbol ?? 1,
            retentionDays: configuration.futureTickPrediction?.shadowStorage?.retentionDays ?? 90,
          },
        },

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
            configuration.validation?.momentumMaximumDrawdown ?? 1,
          momentumHighestPriceTolerance:
            configuration.validation?.momentumHighestPriceTolerance ?? 0.997,
          pullbackMinimumPriceRatio:
            configuration.validation?.pullbackMinimumPriceRatio ?? 0.998,
          maximumPullbackGain:
            configuration.validation?.maximumPullbackGain ?? 1,
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
          ema9BelowEma21Score: configuration.evaluation?.ema9BelowEma21Score ?? 10,
          priceBelowVwapScore: configuration.evaluation?.priceBelowVwapScore ?? 5,
          superTrendBearishScore: configuration.evaluation?.superTrendBearishScore ?? 5,
          minusDiAbovePlusDiScore: configuration.evaluation?.minusDiAbovePlusDiScore ?? 5,
          lastCandleBearishScore: configuration.evaluation?.lastCandleBearishScore ?? 5,
          lowerLowScore: configuration.evaluation?.lowerLowScore ?? 3,

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
          momentumStochasticBullishScore: configuration.evaluation?.momentumStochasticBullishScore ?? 3,
          momentumStochasticBearishScore: configuration.evaluation?.momentumStochasticBearishScore ?? 0,
          pullbackStochasticBullishScore: configuration.evaluation?.pullbackStochasticBullishScore ?? 3,
          pullbackStochasticBearishScore: configuration.evaluation?.pullbackStochasticBearishScore ?? 0,
          momentumAroonBullishScore: configuration.evaluation?.momentumAroonBullishScore ?? 3,
          momentumAroonBearishScore: configuration.evaluation?.momentumAroonBearishScore ?? 0,
          pullbackAroonBullishScore: configuration.evaluation?.pullbackAroonBullishScore ?? 3,
          pullbackAroonBearishScore: configuration.evaluation?.pullbackAroonBearishScore ?? 0,
          momentumParabolicSARBullishScore: configuration.evaluation?.momentumParabolicSARBullishScore ?? 3,
          momentumParabolicSARBearishScore: configuration.evaluation?.momentumParabolicSARBearishScore ?? 0,
          pullbackParabolicSARBullishScore: configuration.evaluation?.pullbackParabolicSARBullishScore ?? 3,
          pullbackParabolicSARBearishScore: configuration.evaluation?.pullbackParabolicSARBearishScore ?? 0,
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
          entryMinimumPriceRatio: configuration.virtualTrading?.entryMinimumPriceRatio ?? 0,
          maximumDrawdownPercent: configuration.virtualTrading?.maximumDrawdownPercent ?? 100,
          highestPriceMinimumRatio: configuration.virtualTrading?.highestPriceMinimumRatio ?? 0,
          positiveRatioWeight: configuration.virtualTrading?.positiveRatioWeight ?? 40,
          aboveEntryRatioWeight: configuration.virtualTrading?.aboveEntryRatioWeight ?? 30,
          maximumHigherHighBonus: configuration.virtualTrading?.maximumHigherHighBonus ?? 15,
          maximumConsecutivePositiveBonus: configuration.virtualTrading?.maximumConsecutivePositiveBonus ?? 15,
          volatilityVeryLowThreshold: configuration.virtualTrading?.volatilityVeryLowThreshold ?? 0.2,
          volatilityLowThreshold: configuration.virtualTrading?.volatilityLowThreshold ?? 0.5,
          volatilityMediumThreshold: configuration.virtualTrading?.volatilityMediumThreshold ?? 0.8,
          volatilityHighThreshold: configuration.virtualTrading?.volatilityHighThreshold ?? 1.2,
          volatilityVeryHighThreshold: configuration.virtualTrading?.volatilityVeryHighThreshold ?? 2,
          fallbackAtrPercent: configuration.virtualTrading?.fallbackAtrPercent ?? 0.5,
          maximumPullbackGainPercent: configuration.virtualTrading?.maximumPullbackGainPercent ?? 100,
        },

        exit: {
          atrExitMultiplier: configuration.exit?.atrExitMultiplier ?? 0.4,
          minimumProfitPercent:
            configuration.exit?.minimumProfitPercent ?? 0.25,
          trailingActivationNetProfit:
            configuration.exit?.trailingActivationNetProfit ?? 0,
          trailingStopAtrMultiplier:
            configuration.exit?.trailingStopAtrMultiplier ?? 0.6,
          trailingProfitRetentionPercent:
            configuration.exit?.trailingProfitRetentionPercent ?? 70,
          enableTradeFailureExit: configuration.exit?.enableTradeFailureExit ?? true,
          tradeFailureMinimumHoldingSeconds: configuration.exit?.tradeFailureMinimumHoldingSeconds ?? 30,
          tradeFailureMinimumFavorableExcursionPercent: configuration.exit?.tradeFailureMinimumFavorableExcursionPercent ?? 0.25,
          tradeFailureMaximumAdverseExcursionPercent: configuration.exit?.tradeFailureMaximumAdverseExcursionPercent ?? 1,
          tradeFailureMaximumAdverseAtrMultiplier: configuration.exit?.tradeFailureMaximumAdverseAtrMultiplier ?? 1.5,
          tradeFailureHealthScoreThreshold: configuration.exit?.tradeFailureHealthScoreThreshold ?? 45,
          tradeFailureRequireTrendDeterioration: configuration.exit?.tradeFailureRequireTrendDeterioration ?? true,
          tradeFailureRequireMomentumDeterioration: configuration.exit?.tradeFailureRequireMomentumDeterioration ?? true,
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
          enableVirtualTradeTickEmails:
            configuration.reporting?.enableVirtualTradeTickEmails ?? true,
          minimumVirtualTradeTicksForEmail:
            configuration.reporting?.minimumVirtualTradeTicksForEmail ?? 3,
          virtualTradeEmailStages: {
            expired: (configuration.reporting?.virtualTradeEmailStages ?? ['EXPIRED', 'REJECTED', 'EXIT']).some(x => x.toUpperCase() === 'EXPIRED'),
            rejected: (configuration.reporting?.virtualTradeEmailStages ?? ['EXPIRED', 'REJECTED', 'EXIT']).some(x => x.toUpperCase() === 'REJECTED'),
            exit: (configuration.reporting?.virtualTradeEmailStages ?? ['EXPIRED', 'REJECTED', 'EXIT']).some(x => x.toUpperCase() === 'EXIT'),
          },
          exitPostSellTickCount:
            configuration.reporting?.exitPostSellTickCount ?? 3,
          tradeAnalyticsPersistenceIntervalSeconds:
            configuration.reporting?.tradeAnalyticsPersistenceIntervalSeconds ?? 5,
        },
      allStockAnalysisArchive: {
        enabled: configuration.allStockAnalysisArchive?.enabled ?? false,
        path: configuration.allStockAnalysisArchive?.path ?? 'TradeAnalysis/AllStocks',
        samplingIntervalSecondsPerSymbol: configuration.allStockAnalysisArchive?.samplingIntervalSecondsPerSymbol ?? 15,
        flushIntervalSeconds: configuration.allStockAnalysisArchive?.flushIntervalSeconds ?? 15,
        maximumBufferedRecords: configuration.allStockAnalysisArchive?.maximumBufferedRecords ?? 5000,
        chunkMinutes: configuration.allStockAnalysisArchive?.chunkMinutes ?? 15,
        retentionDays: configuration.allStockAnalysisArchive?.retentionDays ?? 30,
        recordStateChanges: configuration.allStockAnalysisArchive?.recordStateChanges ?? false,
        includeConfigurationSnapshot: configuration.allStockAnalysisArchive?.includeConfigurationSnapshot ?? true,
        recordLoadingCandidates: configuration.allStockAnalysisArchive?.recordLoadingCandidates ?? true,
      },
    });

    this.form.patchValue(patch, { emitEvent: false });

    this.applyTradingStrictnessProfile(
      (configuration.tradingStrictnessProfile ??
        'VeryLoose') as TradingStrictnessProfile,
    );
    this.form.markAsPristine();
  }

  // ======================================================
  // Helpers
  // ======================================================

  private normalizeExchange(exchange: unknown): string {
    const value = String(exchange ?? '').trim().toUpperCase();
    return value === 'BSE' || value === 'NSE' ? value : 'NSE';
  }

  private readEquityProfile(): InstrumentTradingSettings {
    const value = this.form.getRawValue() as any;
    return {
      exchange: this.normalizeExchange(value.exchange),
      productType: String(value.productType ?? 'INTRADAY'),
      orderType: String(value.orderType ?? 'MARKET'),
      duration: String(value.duration ?? 'DAY'),
      minimumPrice: Number(value.minimumPrice ?? 50),
      minimumVolume: Number(value.minimumVolume ?? 500000),
      atrStopMultiplier: Number(value.atrStopMultiplier ?? 1.2),
      atrTargetMultiplier: Number(value.atrTargetMultiplier ?? 2.4),
      maximumStopPercent: Number(value.maximumStopPercent ?? 1.5),
      minimumStopPercent: Number(value.minimumStopPercent ?? 0.5),
      minimumRiskReward: Number(value.minimumRiskReward ?? 1.5),
      allowLong: !!value.allowLong,
      allowShort: !!value.allowShort,
      riskPercentage: Number(value.riskPercentage ?? 2),
      maxCapitalPerTradePercent: Number(value.maxCapitalPerTradePercent ?? 10),
      minimumNetProfit: Number(value.minimumNetProfit ?? 5),
      minimumRoiPercent: Number(value.minimumRoiPercent ?? 0.3),
      maximumChargesPerTrade: Number(value.maximumChargesPerTrade ?? 100),
      minimumConfidence: Number(value.validation?.minimumConfidence ?? 65),
      minimumFinalScore: Number(value.validation?.minimumFinalScore ?? 70),
      exitOrderTimeoutSeconds: Number(value.exitOrderTimeoutSeconds ?? 10),
      maxMarketDataAgeSeconds: Number(value.maxMarketDataAgeSeconds ?? 15),
      maximumExitRetries: Number(value.maximumExitRetries ?? 5),
      maximumSpreadPercent: Number(value.maximumSpreadPercent ?? 1.5),
      maximumSpreadAmount: Number(value.maximumSpreadAmount ?? 5),
      minimumBid: Number(value.minimumBid ?? 0),
      minimumAsk: Number(value.minimumAsk ?? 0),
      maximumOpenPositions: Number(value.maximumOpenPositions ?? 3),
      maximumRiskPerUnderlying: Number(value.maximumRiskPerUnderlying ?? 2500),
      maximumPositionsPerUnderlying: Number(value.maximumPositionsPerUnderlying ?? 1),
      maximumMarginUtilizationPercent: Number(value.maximumMarginUtilizationPercent ?? 70),
      forceSquareOffBuffer: this.minutesToTimeSpan(value.forceSquareOffBufferMinutes),
      evaluation: value.evaluation,
      validation: value.validation,
    };
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

  private removeNullishValues<T>(value: T): T {
    if (Array.isArray(value)) {
      return value
        .filter((item) => item !== null && item !== undefined)
        .map((item) => this.removeNullishValues(item)) as T;
    }

    if (value && typeof value === 'object') {
      const result: Record<string, unknown> = {};
      Object.entries(value as Record<string, unknown>).forEach(([key, item]) => {
        if (item !== null && item !== undefined) {
          result[key] = this.removeNullishValues(item);
        }
      });
      return result as T;
    }

    return value;
  }

  private toTimeInput(value: string | null | undefined): string | undefined {
    if (!value) {
      return undefined;
    }

    return value.substring(0, 5);
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

  private parseCsvValues(value: string | null | undefined): string[] {
    return String(value ?? '')
      .split(/[\n,]/)
      .map((item) => item.trim())
      .filter(Boolean);
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


    const virtualEmailStages = [
      ...(value.reporting?.virtualTradeEmailStages?.expired ? ['EXPIRED'] : []),
      ...(value.reporting?.virtualTradeEmailStages?.rejected ? ['REJECTED'] : []),
      ...(value.reporting?.virtualTradeEmailStages?.exit ? ['EXIT'] : []),
    ];

    const configuration: TradingConfiguration = {
      id: 'DEFAULT',
      tradingStrictnessProfile: (value.tradingStrictnessProfile ??
        'VeryLoose') as TradingStrictnessProfile,

      equity: this.readEquityProfile(),

      enableAutoTrading: value.enableAutoTrading ?? false,

      paperTrading: value.paperTrading ?? false,

      enableVirtualTrading: value.enableVirtualTrading ?? false,

      enableNotification: value.enableNotification ?? false,

      strategy: value.strategy!,

      riskPercentage: Number(value.riskPercentage),

      maxCapitalPerTradePercent: Number(value.maxCapitalPerTradePercent),

      maxDailyLoss: Number(value.maxDailyLoss),

      maxDailyTrades: Number(value.maxDailyTrades),

      cooldownMinutes: Number(value.cooldownMinutes),

      ignoreMarketHours: value.ignoreMarketHours ?? false,

      marketOpenTime: this.toTimeSpan(value.marketOpenTime),

      marketCloseTime: this.toTimeSpan(value.marketCloseTime),

      maxBrokerFailuresBeforeKillSwitch: Number(value.maxBrokerFailuresBeforeKillSwitch ?? 5),
      brokerFailureWindowMinutes: Number(value.brokerFailureWindowMinutes ?? 2),
      intradayEntryCutoffTime: this.toTimeSpan(value.intradayEntryCutoffTime),
      equityMisAutoSquareOffTime: this.toTimeSpan(value.equityMisAutoSquareOffTime),
      roboAutoSquareOffTime: this.toTimeSpan(value.roboAutoSquareOffTime),
      casTransitionStart: this.toTimeSpan(value.casTransitionStart),
      casOrderEntryStart: this.toTimeSpan(value.casOrderEntryStart),
      casMarketOnlyEnd: this.toTimeSpan(value.casMarketOnlyEnd),
      casLimitOnlyEnd: this.toTimeSpan(value.casLimitOnlyEnd),
      casRandomCloseSafetyCutoff: this.toTimeSpan(value.casRandomCloseSafetyCutoff),
      casEnd: this.toTimeSpan(value.casEnd),
      casPostCloseEnd: this.toTimeSpan(value.casPostCloseEnd),
      casPriceBandPercent: Number(value.casPriceBandPercent ?? 3),
      maximumTotalOpenRisk: Number(value.maximumTotalOpenRisk ?? 10000),
      maximumMarginUtilizationPercent: Number(value.globalMaximumMarginUtilizationPercent ?? 70),
      marketTimeZoneId: String(value.marketTimeZoneId ?? 'Asia/Kolkata'),
      tradingHolidays: this.parseCsvValues(value.tradingHolidaysText),
      emergencyMarginUtilizationPercent: Number(value.emergencyMarginUtilizationPercent ?? 85),
      enableTradingKillSwitchPersistence: (value.enableTradingKillSwitchPersistence ?? true),
      enableGlobalRiskLimits: (value.enableGlobalRiskLimits ?? true),
      riskReservationSeconds: Number(value.riskReservationSeconds ?? 10),
      includeUnrealizedPnlInDailyLoss: (value.includeUnrealizedPnlInDailyLoss ?? true),
      requireClosedHigherTimeframeCandles: (value.requireClosedHigherTimeframeCandles ?? true),
      enablePaperMarginSimulation: (value.enablePaperMarginSimulation ?? true),
      quoteMaxTokensPerRequest: Number(value.quoteMaxTokensPerRequest ?? 50),
      quoteRequestsPerSecond: Number(value.quoteRequestsPerSecond ?? 1),
      maximumSlippagePercent: Number(value.maximumSlippagePercent ?? 0.5),
      enableEntryExecutionRevalidation: (value.enableEntryExecutionRevalidation ?? true),
      entryRevalidationMaxAdverseMovePercent: Number(value.entryRevalidationMaxAdverseMovePercent ?? 0.2),
      entryRevalidationMaxAdverseMoveAtr: Number(value.entryRevalidationMaxAdverseMoveAtr ?? 0.25),
      entryRevalidationMaxAgeSeconds: Number(value.entryRevalidationMaxAgeSeconds ?? 10),
      enableEntryExecutionAudit: (value.enableEntryExecutionAudit ?? true),
      entryExecutionAuditPostFillTickCount: Number(value.entryExecutionAuditPostFillTickCount ?? 20),
      rejectDuplicateOrderIntent: (value.rejectDuplicateOrderIntent ?? true),
      enableScripConsentForCashOrders: (value.enableScripConsentForCashOrders ?? true),
      orderIntentRecoveryIntervalSeconds: Number(value.orderIntentRecoveryIntervalSeconds ?? 5),
      orderIntentRecoveryInitialDelaySeconds: Number(value.orderIntentRecoveryInitialDelaySeconds ?? 2),
      orderIntentUnknownOrderExpiryMinutes: Number(value.orderIntentUnknownOrderExpiryMinutes ?? 2),
      webSocketHeartbeatSeconds: Number(value.webSocketHeartbeatSeconds ?? 10),
      webSocketPongTimeoutSeconds: Number(value.webSocketPongTimeoutSeconds ?? 30),
      webSocketRetryInitialSeconds: Number(value.webSocketRetryInitialSeconds ?? 10),
      webSocketRetryMaxSeconds: Number(value.webSocketRetryMaxSeconds ?? 60),
      brokerPositionConfirmationDelaySeconds: Number(value.brokerPositionConfirmationDelaySeconds ?? 1),
      squareOffRetryDelaySeconds: Number(value.squareOffRetryDelaySeconds ?? 1),
      stopLossConfirmationSeconds: Number(value.stopLossConfirmationSeconds ?? 2),
      capitalAllocationBaseMultiplier: Number(value.capitalAllocationBaseMultiplier ?? 0.25),
      capitalAllocationConfidenceMultiplier: Number(value.capitalAllocationConfidenceMultiplier ?? 0.75),
      eliteMovementScore: Number(value.eliteMovementScore ?? 95),
      strongMovementScore: Number(value.strongMovementScore ?? 90),
      eliteCapitalBonus: Number(value.eliteCapitalBonus ?? 0.1),
      strongCapitalBonus: Number(value.strongCapitalBonus ?? 0.05),
      maximumObservedDrawdownPercent: Number(value.maximumObservedDrawdownPercent ?? 0.25),
      brokerBalanceRefreshSeconds: Number(value.brokerBalanceRefreshSeconds ?? 30),


      excludedSymbols: this.parseExcludedSymbols(value.excludedSymbolsText),

      watchListRefreshMinutes: Number(value.watchListRefreshMinutes),

      minPrice: Number(value.minPrice ?? 50),

      minVolume: Number(value.minVolume ?? 500000),

      maxCandidates: Number(value.maxCandidates ?? 100),

      minimumRoiPercent: Number(value.minimumRoiPercent ?? 0.3),

      minimumNetProfit: Number(value.minimumNetProfit ?? 5),

      enableLiveTradingPerformanceGate:
        value.enableLiveTradingPerformanceGate ?? false,
      minimumLiveTradingPerformanceTrades: Number(
        value.minimumLiveTradingPerformanceTrades ?? 10,
      ),
      minimumLiveTradingWinRate: Number(value.minimumLiveTradingWinRate ?? 55),
      minimumLiveTradingProfitFactor: Number(
        value.minimumLiveTradingProfitFactor ?? 1.2,
      ),
      minimumLiveTradingNetProfit: Number(
        value.minimumLiveTradingNetProfit ?? 0,
      ),
      minimumLiveTradingRiskReward: Number(
        value.minimumLiveTradingRiskReward ?? 1.5,
      ),
      minimumLiveTradingConfidence: Number(
        value.minimumLiveTradingConfidence ?? 60,
      ),
      minimumRecentLiveTradingTrades: Number(
        value.minimumRecentLiveTradingTrades ?? 5,
      ),
      requirePositiveRecentLiveTradingNetProfit:
        value.requirePositiveRecentLiveTradingNetProfit ?? true,
      requireBestStrategyMatchForLiveTrading:
        value.requireBestStrategyMatchForLiveTrading ?? true,
      requireRecentPerformanceToRetainLiveTradingEligibility:
        value.requireRecentPerformanceToRetainLiveTradingEligibility ?? true,
      minimumRecentLiveTradingWinRateToRetainEligibility: Number(
        value.minimumRecentLiveTradingWinRateToRetainEligibility ?? 40,
      ),
      minimumRecentLiveTradingProfitFactorToRetainEligibility: Number(
        value.minimumRecentLiveTradingProfitFactorToRetainEligibility ?? 0.9,
      ),
      enablePaperTradingPerformanceGate:
        value.enablePaperTradingPerformanceGate ?? true,
      minimumPaperTradingPerformanceTrades: Number(
        value.minimumPaperTradingPerformanceTrades ?? 3,
      ),
      minimumPaperTradingWinRate: Number(
        value.minimumPaperTradingWinRate ?? 45,
      ),
      minimumPaperTradingProfitFactor: Number(
        value.minimumPaperTradingProfitFactor ?? 0.8,
      ),
      minimumPaperTradingNetProfit: Number(
        value.minimumPaperTradingNetProfit ?? 0,
      ),
      minimumPaperTradingRiskReward: Number(
        value.minimumPaperTradingRiskReward ?? 1,
      ),
      minimumPaperTradingConfidence: Number(
        value.minimumPaperTradingConfidence ?? 45,
      ),
      requireBestStrategyMatchForPaperTrading:
        value.requireBestStrategyMatchForPaperTrading ?? false,

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

      visibleColumns: this.parseCsvValues(value.visibleColumnsText),

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
      enableStochastic: value.enableStochastic ?? true,
      stochasticPeriod: Number(value.stochasticPeriod ?? 14),
      stochasticKPeriod: Number(value.stochasticKPeriod ?? 3),
      stochasticDPeriod: Number(value.stochasticDPeriod ?? 3),
      stochasticOversold: Number(value.stochasticOversold ?? 15),
      stochasticOverbought: Number(value.stochasticOverbought ?? 85),
      enableAroon: value.enableAroon ?? true,
      aroonPeriod: Number(value.aroonPeriod ?? 25),
      aroonBullishThreshold: Number(value.aroonBullishThreshold ?? 55),
      aroonBearishThreshold: Number(value.aroonBearishThreshold ?? 45),
      enableParabolicSAR: value.enableParabolicSAR ?? true,
      parabolicSARStep: Number(value.parabolicSARStep ?? 0.02),
      parabolicSARMaximum: Number(value.parabolicSARMaximum ?? 0.2),
      coreStrategy: {
        enabled: value.coreStrategy?.enabled ?? true,
        minimumCompletedCandles: Number(value.coreStrategy?.minimumCompletedCandles ?? 3),
        minimumRecoveryScore: Number(value.coreStrategy?.minimumRecoveryScore ?? 40),
        minimumPriceChangePercent: Number(value.coreStrategy?.minimumPriceChangePercent ?? 0),
        minimumBreakoutStrength: Number(value.coreStrategy?.minimumBreakoutStrength ?? 0),
      },
      dynamicEvaluation: {
        enabled: (value.dynamicEvaluation?.enabled ?? true),
        minimumCandleHistory: Number(value.dynamicEvaluation?.minimumCandleHistory ?? 30),
        profileLookbackCandles: Number(value.dynamicEvaluation?.profileLookbackCandles ?? 20),
        minimumEntryScore: Number(value.dynamicEvaluation?.minimumEntryScore ?? 42),
        minimumQuoteOnlyEntryScore: Number(value.dynamicEvaluation?.minimumQuoteOnlyEntryScore ?? 32),
        minimumQuoteOnlySubscriptionScore: Number(value.dynamicEvaluation?.minimumQuoteOnlySubscriptionScore ?? 28),
        maximumQuoteOnlyRiskPenalty: Number(value.dynamicEvaluation?.maximumQuoteOnlyRiskPenalty ?? 8),
        maximumAdaptiveSubscriptions: Number(value.dynamicEvaluation?.maximumAdaptiveSubscriptions ?? 150),
        historicalWarmupCandidates: Number(value.dynamicEvaluation?.historicalWarmupCandidates ?? 100),
        strongTrendThreshold: Number(value.dynamicEvaluation?.strongTrendThreshold ?? 68),
        developingThreshold: Number(value.dynamicEvaluation?.developingThreshold ?? 28),
        maximumEntryScore: Number(value.dynamicEvaluation?.maximumEntryScore ?? 100),
        unknownStockRiskReward: Number(value.dynamicEvaluation?.unknownStockRiskReward ?? 1.5),
        minimumRiskReward: Number(value.dynamicEvaluation?.minimumRiskReward ?? 1.25),
        maximumRiskReward: Number(value.dynamicEvaluation?.maximumRiskReward ?? 3.5),
        minimumNetProfit: Number(value.dynamicEvaluation?.minimumNetProfit ?? 5),
        poorStockNetRewardMultiplier: Number(value.dynamicEvaluation?.poorStockNetRewardMultiplier ?? 1),
        goodStockNetRewardMultiplier: Number(value.dynamicEvaluation?.goodStockNetRewardMultiplier ?? 1.35),
        excellentStockNetRewardMultiplier: Number(value.dynamicEvaluation?.excellentStockNetRewardMultiplier ?? 1.75),
        normalStopAtrMultiplier: Number(value.dynamicEvaluation?.normalStopAtrMultiplier ?? 1.15),
        recoveryStopAtrMultiplier: Number(value.dynamicEvaluation?.recoveryStopAtrMultiplier ?? 1.8),
        maximumStopAtrMultiplier: Number(value.dynamicEvaluation?.maximumStopAtrMultiplier ?? 2.5),
        maximumStructuralStopAtrDistance: Number(value.dynamicEvaluation?.maximumStructuralStopAtrDistance ?? 2.5),
        targetExtensionStepAtr: Number(value.dynamicEvaluation?.targetExtensionStepAtr ?? 0.5),
        maximumTargetExtensionIterations: Number(value.dynamicEvaluation?.maximumTargetExtensionIterations ?? 4),
        strongPerformanceScore: Number(value.dynamicEvaluation?.strongPerformanceScore ?? 70),
        excellentPerformanceScore: Number(value.dynamicEvaluation?.excellentPerformanceScore ?? 85),
        recoveryScoreThreshold: Number(value.dynamicEvaluation?.recoveryScoreThreshold ?? 60),
        minimumRiskMultiplier: Number(value.dynamicEvaluation?.minimumRiskMultiplier ?? 0.35),
        maximumRiskMultiplier: Number(value.dynamicEvaluation?.maximumRiskMultiplier ?? 1.25),
        trendWeight: Number(value.dynamicEvaluation?.trendWeight ?? 18),
        momentumWeight: Number(value.dynamicEvaluation?.momentumWeight ?? 16),
        candleWeight: Number(value.dynamicEvaluation?.candleWeight ?? 14),
        volumeWeight: Number(value.dynamicEvaluation?.volumeWeight ?? 10),
        priceActionWeight: Number(value.dynamicEvaluation?.priceActionWeight ?? 14),
        recoveryWeight: Number(value.dynamicEvaluation?.recoveryWeight ?? 10),
        regimeWeight: Number(value.dynamicEvaluation?.regimeWeight ?? 8),
        multiTimeframeWeight: Number(value.dynamicEvaluation?.multiTimeframeWeight ?? 8),
        spreadPenaltyWeight: Number(value.dynamicEvaluation?.spreadPenaltyWeight ?? 8),
        exhaustionPenalty: Number(value.dynamicEvaluation?.exhaustionPenalty ?? 12),
        minimumExpectedNetValue: Number(value.dynamicEvaluation?.minimumExpectedNetValue ?? 0),
        minimumEdgeScore: Number(value.dynamicEvaluation?.minimumEdgeScore ?? 45),
        minimumStatisticalConfidence: Number(value.dynamicEvaluation?.minimumStatisticalConfidence ?? 20),
        noTradePenaltyThreshold: Number(value.dynamicEvaluation?.noTradePenaltyThreshold ?? 65),
        maximumRiskWhenStatisticallyUncertain: Number(value.dynamicEvaluation?.maximumRiskWhenStatisticallyUncertain ?? 0.65),
        recentPerformanceWeight: Number(value.dynamicEvaluation?.recentPerformanceWeight ?? 0.35),
        historicalPerformanceWeight: Number(value.dynamicEvaluation?.historicalPerformanceWeight ?? 0.65),
        marketRegimeWeight: Number(value.dynamicEvaluation?.marketRegimeWeight ?? 0.10),
        relativeStrengthWeight: Number(value.dynamicEvaluation?.relativeStrengthWeight ?? 0.10),
      },
      dynamicVirtualTrading: {
        enabled: (value.dynamicVirtualTrading?.enabled ?? true),
        minimumObservationTicks: Number(value.dynamicVirtualTrading?.minimumObservationTicks ?? 3),
        maximumObservationTicks: Number(value.dynamicVirtualTrading?.maximumObservationTicks ?? 40),
        minimumObservationSeconds: Number(value.dynamicVirtualTrading?.minimumObservationSeconds ?? 1),
        maximumObservationSeconds: Number(value.dynamicVirtualTrading?.maximumObservationSeconds ?? 30),
        minimumFavorableTickRatio: Number(value.dynamicVirtualTrading?.minimumFavorableTickRatio ?? 0.52),
        maximumAdverseTickRatio: Number(value.dynamicVirtualTrading?.maximumAdverseTickRatio ?? 0.6),
        recoveryTickRatioBonus: Number(value.dynamicVirtualTrading?.recoveryTickRatioBonus ?? 0.08),
        maximumAdverseMoveAtr: Number(value.dynamicVirtualTrading?.maximumAdverseMoveAtr ?? 0.9),
        recoveryMaximumAdverseMoveAtr: Number(value.dynamicVirtualTrading?.recoveryMaximumAdverseMoveAtr ?? 1.5),
        minimumTickMomentum: Number(value.dynamicVirtualTrading?.minimumTickMomentum ?? 0.05),
        minimumTrendStability: Number(value.dynamicVirtualTrading?.minimumTrendStability ?? 45),
        minimumMovementScore: Number(value.dynamicVirtualTrading?.minimumMovementScore ?? 35),
        maximumNoiseScoreForEntry: Number(value.dynamicVirtualTrading?.maximumNoiseScoreForEntry ?? 75),
        minimumRecoveryScore: Number(value.dynamicVirtualTrading?.minimumRecoveryScore ?? 45),
        minimumBreakoutStrength: Number(value.dynamicVirtualTrading?.minimumBreakoutStrength ?? 35),
        minimumPriceSlope: Number(value.dynamicVirtualTrading?.minimumPriceSlope ?? -0.05),
        minimumProfitAtrBeforeTrailing: Number(value.dynamicVirtualTrading?.minimumProfitAtrBeforeTrailing ?? 0.5),
        baseTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.baseTrailingAtrMultiplier ?? 1.2),
        strongTrendTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.strongTrendTrailingAtrMultiplier ?? 1.5),
        recoveryTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.recoveryTrailingAtrMultiplier ?? 1.8),
        weakTrendTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.weakTrendTrailingAtrMultiplier ?? 0.9),
        minimumTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.minimumTrailingAtrMultiplier ?? 0.7),
        maximumTrailingAtrMultiplier: Number(value.dynamicVirtualTrading?.maximumTrailingAtrMultiplier ?? 2.2),
        minimumExecutionConfidence: Number(value.dynamicVirtualTrading?.minimumExecutionConfidence ?? 35),
        peakProfitRetentionPercent: Number(value.dynamicVirtualTrading?.peakProfitRetentionPercent ?? 60),
        recoveryPeakProfitRetentionPercent: Number(value.dynamicVirtualTrading?.recoveryPeakProfitRetentionPercent ?? 40),
        tickPriceMoveWeight: Number(value.dynamicVirtualTrading?.tickPriceMoveWeight ?? 30),
        tickDirectionWeight: Number(value.dynamicVirtualTrading?.tickDirectionWeight ?? 25),
        tickAccelerationWeight: Number(value.dynamicVirtualTrading?.tickAccelerationWeight ?? 15),
        tickVolumeWeight: Number(value.dynamicVirtualTrading?.tickVolumeWeight ?? 10),
        tickSpreadWeight: Number(value.dynamicVirtualTrading?.tickSpreadWeight ?? 10),
        tickRecoveryWeight: Number(value.dynamicVirtualTrading?.tickRecoveryWeight ?? 10),
        baseCapitalMultiplier: Number(value.dynamicVirtualTrading?.baseCapitalMultiplier ?? 0.35),
        confidenceCapitalMultiplier: Number(value.dynamicVirtualTrading?.confidenceCapitalMultiplier ?? 0.65),
      },
      futureTickPrediction: {
        enabled: value.futureTickPrediction?.enabled ?? true,
        shadowMode: value.futureTickPrediction?.shadowMode ?? false,
        horizonTicks: Number(value.futureTickPrediction?.horizonTicks ?? 5),
        horizonSeconds: Number(value.futureTickPrediction?.horizonSeconds ?? 10),
        minimumFavorableProbability: Number(value.futureTickPrediction?.minimumFavorableProbability ?? 0.48),
        useAdaptiveEntryGate: value.futureTickPrediction?.useAdaptiveEntryGate ?? true,
        strongStrategyScore: Number(value.futureTickPrediction?.strongStrategyScore ?? 70),
        adaptiveMinimumFavorableProbability: Number(value.futureTickPrediction?.adaptiveMinimumFavorableProbability ?? 0.42),
        adaptiveMaximumAdverseProbability: Number(value.futureTickPrediction?.adaptiveMaximumAdverseProbability ?? 0.50),
        adaptiveMinimumContinuationScore: Number(value.futureTickPrediction?.adaptiveMinimumContinuationScore ?? 0.02),
        adaptiveMinimumPredictionConfidence: Number(value.futureTickPrediction?.adaptiveMinimumPredictionConfidence ?? 0.30),
        maximumAdverseProbability: Number(value.futureTickPrediction?.maximumAdverseProbability ?? 0.48),
        minimumContinuationScore: Number(value.futureTickPrediction?.minimumContinuationScore ?? 0.08),
        minimumPredictionConfidence: Number(value.futureTickPrediction?.minimumPredictionConfidence ?? 0.40),
        minimumExpectedNetValue: Number(value.futureTickPrediction?.minimumExpectedNetValue ?? -0.25),
        maximumAdverseMoveAtr: Number(value.futureTickPrediction?.maximumAdverseMoveAtr ?? 0.65),
        minimumVolumeParticipation: Number(value.futureTickPrediction?.minimumVolumeParticipation ?? 0.25),
        immediateFailureScore: Number(value.futureTickPrediction?.immediateFailureScore ?? 35),
        minimumObservedTicksForPostEntryGate: Number(value.futureTickPrediction?.minimumObservedTicksForPostEntryGate ?? 3),
        consecutiveAdverseTicksForEarlyExit: Number(value.futureTickPrediction?.consecutiveAdverseTicksForEarlyExit ?? 3),
        shadowStorage: {
          enabled: value.futureTickPrediction?.shadowStorage?.enabled ?? true,
          path: String(value.futureTickPrediction?.shadowStorage?.path ?? 'TradeAnalysis/FutureTickPrediction'),
          minimumRecordIntervalSecondsPerSymbol: Number(
            value.futureTickPrediction?.shadowStorage?.minimumRecordIntervalSecondsPerSymbol ?? 1,
          ),
          retentionDays: Number(value.futureTickPrediction?.shadowStorage?.retentionDays ?? 90),
        },
      },
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
          value.validation?.momentumMaximumDrawdown ?? 1,
        ),
        momentumHighestPriceTolerance: Number(
          value.validation?.momentumHighestPriceTolerance ?? 0.997,
        ),
        pullbackMinimumPriceRatio: Number(
          value.validation?.pullbackMinimumPriceRatio ?? 0.998,
        ),
        maximumPullbackGain: Number(value.validation?.maximumPullbackGain ?? 1),
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
        ema9BelowEma21Score: Number(value.evaluation?.ema9BelowEma21Score ?? 10),
        priceBelowVwapScore: Number(value.evaluation?.priceBelowVwapScore ?? 5),
        superTrendBearishScore: Number(value.evaluation?.superTrendBearishScore ?? 5),
        minusDiAbovePlusDiScore: Number(value.evaluation?.minusDiAbovePlusDiScore ?? 5),
        lastCandleBearishScore: Number(value.evaluation?.lastCandleBearishScore ?? 5),
        lowerLowScore: Number(value.evaluation?.lowerLowScore ?? 3),
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
        momentumStochasticBullishScore: Number(value.evaluation?.momentumStochasticBullishScore ?? 3),
        momentumStochasticBearishScore: Number(value.evaluation?.momentumStochasticBearishScore ?? 0),
        pullbackStochasticBullishScore: Number(value.evaluation?.pullbackStochasticBullishScore ?? 3),
        pullbackStochasticBearishScore: Number(value.evaluation?.pullbackStochasticBearishScore ?? 0),
        momentumAroonBullishScore: Number(value.evaluation?.momentumAroonBullishScore ?? 3),
        momentumAroonBearishScore: Number(value.evaluation?.momentumAroonBearishScore ?? 0),
        pullbackAroonBullishScore: Number(value.evaluation?.pullbackAroonBullishScore ?? 3),
        pullbackAroonBearishScore: Number(value.evaluation?.pullbackAroonBearishScore ?? 0),
        momentumParabolicSARBullishScore: Number(value.evaluation?.momentumParabolicSARBullishScore ?? 3),
        momentumParabolicSARBearishScore: Number(value.evaluation?.momentumParabolicSARBearishScore ?? 0),
        pullbackParabolicSARBullishScore: Number(value.evaluation?.pullbackParabolicSARBullishScore ?? 3),
        pullbackParabolicSARBearishScore: Number(value.evaluation?.pullbackParabolicSARBearishScore ?? 0),
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
        entryMinimumPriceRatio: Number(value.virtualTrading?.entryMinimumPriceRatio ?? 0),
        maximumDrawdownPercent: Number(value.virtualTrading?.maximumDrawdownPercent ?? 100),
        highestPriceMinimumRatio: Number(value.virtualTrading?.highestPriceMinimumRatio ?? 0),
        positiveRatioWeight: Number(value.virtualTrading?.positiveRatioWeight ?? 40),
        aboveEntryRatioWeight: Number(value.virtualTrading?.aboveEntryRatioWeight ?? 30),
        maximumHigherHighBonus: Number(value.virtualTrading?.maximumHigherHighBonus ?? 15),
        maximumConsecutivePositiveBonus: Number(value.virtualTrading?.maximumConsecutivePositiveBonus ?? 15),
        volatilityVeryLowThreshold: Number(value.virtualTrading?.volatilityVeryLowThreshold ?? 0.2),
        volatilityLowThreshold: Number(value.virtualTrading?.volatilityLowThreshold ?? 0.5),
        volatilityMediumThreshold: Number(value.virtualTrading?.volatilityMediumThreshold ?? 0.8),
        volatilityHighThreshold: Number(value.virtualTrading?.volatilityHighThreshold ?? 1.2),
        volatilityVeryHighThreshold: Number(value.virtualTrading?.volatilityVeryHighThreshold ?? 2),
        fallbackAtrPercent: Number(value.virtualTrading?.fallbackAtrPercent ?? 0.5),
        maximumPullbackGainPercent: Number(value.virtualTrading?.maximumPullbackGainPercent ?? 100),
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
        trailingProfitRetentionPercent: Number(
          value.exit?.trailingProfitRetentionPercent ?? 70,
        ),
        enableTradeFailureExit: value.exit?.enableTradeFailureExit ?? true,
        tradeFailureMinimumHoldingSeconds: Number(value.exit?.tradeFailureMinimumHoldingSeconds ?? 30),
        tradeFailureMinimumFavorableExcursionPercent: Number(value.exit?.tradeFailureMinimumFavorableExcursionPercent ?? 0.25),
        tradeFailureMaximumAdverseExcursionPercent: Number(value.exit?.tradeFailureMaximumAdverseExcursionPercent ?? 1),
        tradeFailureMaximumAdverseAtrMultiplier: Number(value.exit?.tradeFailureMaximumAdverseAtrMultiplier ?? 1.5),
        tradeFailureHealthScoreThreshold: Number(value.exit?.tradeFailureHealthScoreThreshold ?? 45),
        tradeFailureRequireTrendDeterioration: value.exit?.tradeFailureRequireTrendDeterioration ?? true,
        tradeFailureRequireMomentumDeterioration: value.exit?.tradeFailureRequireMomentumDeterioration ?? true,
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
        enableVirtualTradeTickEmails: value.reporting?.enableVirtualTradeTickEmails ?? true,
        minimumVirtualTradeTicksForEmail: Number(
          value.reporting?.minimumVirtualTradeTicksForEmail ?? 3,
        ),
        virtualTradeEmailStages: virtualEmailStages,
        exitPostSellTickCount: Number(value.reporting?.exitPostSellTickCount ?? 3),
        tradeAnalyticsPersistenceIntervalSeconds: Number(value.reporting?.tradeAnalyticsPersistenceIntervalSeconds ?? 5),
        tradeAnalysisPublicBaseUrl: String(value.reporting?.tradeAnalysisPublicBaseUrl ?? ''),
      },
      allStockAnalysisArchive: {
        enabled: value.allStockAnalysisArchive?.enabled ?? false,
        path: String(value.allStockAnalysisArchive?.path ?? 'TradeAnalysis/AllStocks'),
        samplingIntervalSecondsPerSymbol: Number(value.allStockAnalysisArchive?.samplingIntervalSecondsPerSymbol ?? 15),
        flushIntervalSeconds: Number(value.allStockAnalysisArchive?.flushIntervalSeconds ?? 15),
        maximumBufferedRecords: Number(value.allStockAnalysisArchive?.maximumBufferedRecords ?? 5000),
        chunkMinutes: Number(value.allStockAnalysisArchive?.chunkMinutes ?? 15),
        retentionDays: Number(value.allStockAnalysisArchive?.retentionDays ?? 30),
        recordStateChanges: value.allStockAnalysisArchive?.recordStateChanges ?? false,
        includeConfigurationSnapshot: value.allStockAnalysisArchive?.includeConfigurationSnapshot ?? true,
        recordLoadingCandidates: value.allStockAnalysisArchive?.recordLoadingCandidates ?? true,
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

    const timestamp = new Date().toISOString().replace(/[:.]/g, '-');

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
