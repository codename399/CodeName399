import { Component, OnInit, inject } from '@angular/core';

import { CommonModule } from '@angular/common';

import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../../services/toast.service';
import { TradingConfiguration } from '../../../models/trading-configuration';
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
  readonly #fb = inject(FormBuilder);

  readonly #angel = inject(AngelOneService);

  readonly #toastService = inject(ToastService);

  readonly #router = inject(Router);

  loading = false;

  saving = false;

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

  form = this.#fb.group({
    enableAutoTrading: [{ value: false, disabled: false }],

    paperTrading: [true],

    enableNotification: [true],

    strategy: [1, Validators.required],

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

    minimumRoiPercent: [0.3, [Validators.required, Validators.min(0), Validators.max(100)]],

    minimumNetProfit: [5, Validators.required],

    autoSquareOff: [true],

    paperTradingBalance: [100000, Validators.required],

    virtualTradeObservationSeconds: [20, Validators.required],

    virtualTradeExpirySeconds: [60, Validators.required],

    minimumVirtualProfitPercent: [0.25, [Validators.required, Validators.min(0), Validators.max(100)]],

    maximumVirtualPullbackPercent: [0.5, [Validators.required, Validators.min(0), Validators.max(100)]],

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
      minimumMovementScore: [45, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumConfidence: [65, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumRiskReward: [1.5, [Validators.required, Validators.min(0)]],
      minimumTrendStrength: [50, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumTrendStability: [55, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumRecoveryScore: [50, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumVolatilityScore: [30, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumNoiseScore: [45, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumBreakoutStrength: [45, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumRSI: [50, [Validators.required, Validators.min(0), Validators.max(100)]],
      maximumRSI: [72, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumVolumeMultiplier: [1.5, [Validators.required, Validators.min(0)]],
      minimumPositiveTickRatio: [0.55, [Validators.required, Validators.min(0), Validators.max(1)]],
      minimumAboveEntryRatio: [0.55, [Validators.required, Validators.min(0), Validators.max(1)]],
      minimumHigherHighs: [2, [Validators.required, Validators.min(0)]],
      minimumConsecutivePositiveTicks: [2, [Validators.required, Validators.min(0)]],
      maximumConsecutiveNegativeTicks: [6, [Validators.required, Validators.min(0)]],
      maximumDrawdownPercent: [0.4, [Validators.required, Validators.min(0), Validators.max(100)]],
    }),

    evaluation: this.#fb.group({
      strongAdx: [30, [Validators.required, Validators.min(0), Validators.max(100)]],
      mediumAdx: [25, [Validators.required, Validators.min(0), Validators.max(100)]],
      lowChoppiness: [38, [Validators.required, Validators.min(0), Validators.max(100)]],
      highChoppiness: [55, [Validators.required, Validators.min(0), Validators.max(100)]],
      highRelativeVolume: [2, [Validators.required, Validators.min(0)]],
      mediumRelativeVolume: [1.5, [Validators.required, Validators.min(0)]],
      lowRelativeVolume: [1.2, [Validators.required, Validators.min(0)]],
      excellentScore: [90, [Validators.required, Validators.min(0), Validators.max(100)]],
      goodScore: [80, [Validators.required, Validators.min(0), Validators.max(100)]],
      averageScore: [70, [Validators.required, Validators.min(0), Validators.max(100)]],
    }),

    virtualTrading: this.#fb.group({
      warmupSeconds: [8, [Validators.required, Validators.min(0)]],
      observationSeconds: [15, [Validators.required, Validators.min(0)]],
      maximumObservationSeconds: [45, [Validators.required, Validators.min(0)]],
      tickWindow: [20, [Validators.required, Validators.min(0)]],
      entryLossPercent: [0.2, [Validators.required, Validators.min(0), Validators.max(100)]],
      highestPriceTolerance: [0.3, [Validators.required, Validators.min(0), Validators.max(100)]],
    }),

    exit: this.#fb.group({
      atrExitMultiplier: [0.4, [Validators.required, Validators.min(0), Validators.max(100)]],
      minimumProfitPercent: [0.25, [Validators.required, Validators.min(0), Validators.max(100)]],
    }),
  });

  enableAutoTradingFormControl = this.form?.controls?.enableAutoTrading;
  enableAutoTradingPreviousValue = this.enableAutoTradingFormControl?.value;

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

    this.loadConfiguration();
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

        virtualTradeObservationSeconds: configuration.virtualTradeObservationSeconds,

        virtualTradeExpirySeconds: configuration.virtualTradeExpirySeconds,

        minimumVirtualProfitPercent: configuration.minimumVirtualProfitPercent,

        maximumVirtualPullbackPercent: configuration.maximumVirtualPullbackPercent,

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
          minimumMovementScore: configuration.validation?.minimumMovementScore ?? 45,
          minimumConfidence: configuration.validation?.minimumConfidence ?? 65,
          minimumRiskReward: configuration.validation?.minimumRiskReward ?? 1.5,
          minimumTrendStrength: configuration.validation?.minimumTrendStrength ?? 50,
          minimumTrendStability: configuration.validation?.minimumTrendStability ?? 55,
          minimumRecoveryScore: configuration.validation?.minimumRecoveryScore ?? 50,
          minimumVolatilityScore: configuration.validation?.minimumVolatilityScore ?? 30,
          minimumNoiseScore: configuration.validation?.minimumNoiseScore ?? 45,
          minimumBreakoutStrength: configuration.validation?.minimumBreakoutStrength ?? 45,
          minimumRSI: configuration.validation?.minimumRSI ?? 50,
          maximumRSI: configuration.validation?.maximumRSI ?? 72,
          minimumVolumeMultiplier: configuration.validation?.minimumVolumeMultiplier ?? 1.5,
          minimumPositiveTickRatio: configuration.validation?.minimumPositiveTickRatio ?? 0.55,
          minimumAboveEntryRatio: configuration.validation?.minimumAboveEntryRatio ?? 0.55,
          minimumHigherHighs: configuration.validation?.minimumHigherHighs ?? 2,
          minimumConsecutivePositiveTicks: configuration.validation?.minimumConsecutivePositiveTicks ?? 2,
          maximumConsecutiveNegativeTicks: configuration.validation?.maximumConsecutiveNegativeTicks ?? 6,
          maximumDrawdownPercent: configuration.validation?.maximumDrawdownPercent ?? 0.4,
        },

        evaluation: {
          strongAdx: configuration.evaluation?.strongAdx ?? 30,
          mediumAdx: configuration.evaluation?.mediumAdx ?? 25,
          lowChoppiness: configuration.evaluation?.lowChoppiness ?? 38,
          highChoppiness: configuration.evaluation?.highChoppiness ?? 55,
          highRelativeVolume: configuration.evaluation?.highRelativeVolume ?? 2,
          mediumRelativeVolume: configuration.evaluation?.mediumRelativeVolume ?? 1.5,
          lowRelativeVolume: configuration.evaluation?.lowRelativeVolume ?? 1.2,
          excellentScore: configuration.evaluation?.excellentScore ?? 90,
          goodScore: configuration.evaluation?.goodScore ?? 80,
          averageScore: configuration.evaluation?.averageScore ?? 70,
        },

        virtualTrading: {
          warmupSeconds: configuration.virtualTrading?.warmupSeconds ?? 8,
          observationSeconds: configuration.virtualTrading?.observationSeconds ?? 15,
          maximumObservationSeconds: configuration.virtualTrading?.maximumObservationSeconds ?? 45,
          tickWindow: configuration.virtualTrading?.tickWindow ?? 20,
          entryLossPercent: configuration.virtualTrading?.entryLossPercent ?? 0.2,
          highestPriceTolerance: configuration.virtualTrading?.highestPriceTolerance ?? 0.3,
        },

        exit: {
          atrExitMultiplier: configuration.exit?.atrExitMultiplier ?? 0.4,
          minimumProfitPercent: configuration.exit?.minimumProfitPercent ?? 0.25,
        },
      },
      {
        emitEvent: false,
      },
    );
  }

  // ======================================================
  // Helpers
  // ======================================================

  private toTimeInput(value: string | null | undefined): string {
    if (!value) {
      return '';
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

  private toTimeSpan(value: string | null): string {
    if (!value) {
      return '00:00:00';
    }

    return `${value}:00`;
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

    const configuration: TradingConfiguration = {
      id: 'DEFAULT',

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

      virtualTradeObservationSeconds: Number(value.virtualTradeObservationSeconds ?? 20),

      virtualTradeExpirySeconds: Number(value.virtualTradeExpirySeconds ?? 60),

      minimumVirtualProfitPercent: Number(value.minimumVirtualProfitPercent ?? 0.25),

      maximumVirtualPullbackPercent: Number(value.maximumVirtualPullbackPercent ?? 0.5),

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
        minimumMovementScore: Number(value.validation?.minimumMovementScore ?? 45),
        minimumConfidence: Number(value.validation?.minimumConfidence ?? 65),
        minimumRiskReward: Number(value.validation?.minimumRiskReward ?? 1.5),
        minimumTrendStrength: Number(value.validation?.minimumTrendStrength ?? 50),
        minimumTrendStability: Number(value.validation?.minimumTrendStability ?? 55),
        minimumRecoveryScore: Number(value.validation?.minimumRecoveryScore ?? 50),
        minimumVolatilityScore: Number(value.validation?.minimumVolatilityScore ?? 30),
        minimumNoiseScore: Number(value.validation?.minimumNoiseScore ?? 45),
        minimumBreakoutStrength: Number(value.validation?.minimumBreakoutStrength ?? 45),
        minimumRSI: Number(value.validation?.minimumRSI ?? 50),
        maximumRSI: Number(value.validation?.maximumRSI ?? 72),
        minimumVolumeMultiplier: Number(value.validation?.minimumVolumeMultiplier ?? 1.5),
        minimumPositiveTickRatio: Number(value.validation?.minimumPositiveTickRatio ?? 0.55),
        minimumAboveEntryRatio: Number(value.validation?.minimumAboveEntryRatio ?? 0.55),
        minimumHigherHighs: Number(value.validation?.minimumHigherHighs ?? 2),
        minimumConsecutivePositiveTicks: Number(value.validation?.minimumConsecutivePositiveTicks ?? 2),
        maximumConsecutiveNegativeTicks: Number(value.validation?.maximumConsecutiveNegativeTicks ?? 6),
        maximumDrawdownPercent: Number(value.validation?.maximumDrawdownPercent ?? 0.4),
      },
      evaluation: {
        strongAdx: Number(value.evaluation?.strongAdx ?? 30),
        mediumAdx: Number(value.evaluation?.mediumAdx ?? 25),
        lowChoppiness: Number(value.evaluation?.lowChoppiness ?? 38),
        highChoppiness: Number(value.evaluation?.highChoppiness ?? 55),
        highRelativeVolume: Number(value.evaluation?.highRelativeVolume ?? 2),
        mediumRelativeVolume: Number(value.evaluation?.mediumRelativeVolume ?? 1.5),
        lowRelativeVolume: Number(value.evaluation?.lowRelativeVolume ?? 1.2),
        excellentScore: Number(value.evaluation?.excellentScore ?? 90),
        goodScore: Number(value.evaluation?.goodScore ?? 80),
        averageScore: Number(value.evaluation?.averageScore ?? 70),
      },
      virtualTrading: {
        warmupSeconds: Number(value.virtualTrading?.warmupSeconds ?? 8),
        observationSeconds: Number(value.virtualTrading?.observationSeconds ?? 15),
        maximumObservationSeconds: Number(value.virtualTrading?.maximumObservationSeconds ?? 45),
        tickWindow: Number(value.virtualTrading?.tickWindow ?? 20),
        entryLossPercent: Number(value.virtualTrading?.entryLossPercent ?? 0.2),
        highestPriceTolerance: Number(value.virtualTrading?.highestPriceTolerance ?? 0.3),
      },
      exit: {
        atrExitMultiplier: Number(value.exit?.atrExitMultiplier ?? 0.4),
        minimumProfitPercent: Number(value.exit?.minimumProfitPercent ?? 0.25),
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
