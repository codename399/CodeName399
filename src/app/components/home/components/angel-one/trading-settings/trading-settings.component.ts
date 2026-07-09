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

    marketCloseTime: ['15:30', Validators.required],

    excludedSymbolsText: [''],

    watchListRefreshMinutes: [2, Validators.required],

    minPrice: [50, Validators.required],

    minVolume: [500000, Validators.required],

    maxCandidates: [100, Validators.required],

    maximumChargesPerTrade: [100, Validators.required],

    buyTradingInterval: [5, Validators.required],

    sellTradingInterval: [1000, Validators.required],
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

        buyTradingInterval: configuration.buyTradingInterval,

        sellTradingInterval: configuration.sellTradingInterval,
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

      visibleColumns: this.#angel.configuration()?.visibleColumns ?? [],

      maximumChargesPerTrade: Number(value.maximumChargesPerTrade ?? 100),
      lastDailySummarySent: null,
      buyTradingInterval: Number(value.buyTradingInterval ?? 5),
      sellTradingInterval: Number(value.sellTradingInterval ?? 1000),
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
