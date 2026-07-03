import { Component, OnInit, inject } from '@angular/core';

import { CommonModule } from '@angular/common';

import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { MatButtonModule } from '@angular/material/button';

import { MatInputModule } from '@angular/material/input';

import { MatFormFieldModule } from '@angular/material/form-field';

import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { MatSelectModule } from '@angular/material/select';

import { MatDividerModule } from '@angular/material/divider';

import { MatIconModule } from '@angular/material/icon';

import { MatCardModule } from '@angular/material/card';

import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../../services/toast.service';
import { TradingConfiguration } from '../../../models/trading-configuration';
import { AngelOneService } from '../../../services/angel-one.service';

@Component({
  selector: 'app-trading-settings',

  standalone: true,

  imports: [
    CommonModule,

    ReactiveFormsModule,

    MatButtonModule,

    MatInputModule,

    MatFormFieldModule,

    MatSlideToggleModule,

    MatSelectModule,

    MatDividerModule,

    MatIconModule,
    MatCardModule,
  ],

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

    scanIntervalSeconds: [5, Validators.required],

    ignoreMarketHours: [false],

    enableTrailingStop: [true],

    trailingStopPercentage: [1, Validators.required],

    stopLossMultiplier: [1, Validators.required],

    targetMultiplier: [2, Validators.required],

    autoSquareOff: [true],

    marketOpenTime: ['09:15', Validators.required],

    marketCloseTime: ['15:30', Validators.required],

    excludedSymbols: [[]],

    watchListRefreshMinutes: [2, Validators.required],

    minPrice: [50, Validators.required],

    minVolume: [500000, Validators.required],

    minChangePercent: [1, Validators.required],

    maxCandidates: [100, Validators.required],
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

        strategy: configuration.strategy,

        riskPercentage: configuration.riskPercentage,

        maxCapitalPerTrade: configuration.maxCapitalPerTrade,

        maxDailyLoss: configuration.maxDailyLoss,

        maxDailyTrades: configuration.maxDailyTrades,

        cooldownMinutes: configuration.cooldownMinutes,

        scanIntervalSeconds: configuration.scanIntervalSeconds,

        ignoreMarketHours: configuration.ignoreMarketHours,

        enableTrailingStop: configuration.enableTrailingStop,

        trailingStopPercentage: configuration.trailingStopPercentage,

        stopLossMultiplier: configuration.stopLossMultiplier,

        targetMultiplier: configuration.targetMultiplier,

        autoSquareOff: configuration.autoSquareOff,

        marketOpenTime: this.toTimeInput(configuration.marketOpenTime),

        marketCloseTime: this.toTimeInput(configuration.marketCloseTime),

        watchListRefreshMinutes: configuration.watchListRefreshMinutes,

        minPrice: configuration.minPrice,

        minVolume: configuration.minVolume,

        minChangePercent: configuration.minChangePercent,

        maxCandidates: configuration.maxCandidates,
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

  private toTimeSpan(value: string | null): string {
    if (!value) {
      return '00:00:00';
    }

    return `${value}:00`;
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

      scanIntervalSeconds: Number(value.scanIntervalSeconds),

      ignoreMarketHours: value.ignoreMarketHours ?? false,

      enableTrailingStop: value.enableTrailingStop ?? false,

      trailingStopPercentage: Number(value.trailingStopPercentage),

      stopLossMultiplier: Number(value.stopLossMultiplier),

      targetMultiplier: Number(value.targetMultiplier),

      autoSquareOff: value.autoSquareOff ?? false,

      marketOpenTime: this.toTimeSpan(value.marketOpenTime),

      marketCloseTime: this.toTimeSpan(value.marketCloseTime),

      watchListRefreshMinutes: Number(value.watchListRefreshMinutes),

      minPrice: Number(value.minPrice ?? 50),

      minVolume: Number(value.minVolume ?? 500000),

      minChangePercent: Number(value.minChangePercent ?? 1),

      maxCandidates: Number(value.maxCandidates ?? 100),
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
