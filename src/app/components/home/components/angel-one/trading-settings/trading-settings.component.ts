import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ToastService } from '../../../../../services/toast.service';
import { AngelOneService } from '../../../services/angel-one.service';
import { TradingConfiguration } from '../../../models/trading-settings';

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
  readonly #toast = inject(ToastService);

  loading = false;
  saving = false;
  private currentConfiguration: TradingConfiguration | null = null;

  readonly form = this.#fb.group({
    enableAutoTrading: [false],
    paperTrading: [true],
    enableNotification: [true],

    minimumCompletedCandles: [4, [Validators.required, Validators.min(1)]],
    minimumRecoveryScore: [60, [Validators.required, Validators.min(0), Validators.max(100)]],
    minimumPriceChangePercent: [0.05, [Validators.required, Validators.min(0)]],
    minimumBreakoutStrength: [5, [Validators.required, Validators.min(0), Validators.max(100)]],

    riskPercentage: [2, [Validators.required, Validators.min(0)]],
    maxCapitalPerTradePercent: [10, [Validators.required, Validators.min(0), Validators.max(100)]],
    maximumTotalOpenRisk: [10000, [Validators.required, Validators.min(0)]],

    exchange: ['NSE', Validators.required],
    productType: ['INTRADAY', Validators.required],
    orderType: ['MARKET', Validators.required],
    duration: ['DAY', Validators.required],
    atrStopMultiplier: [1.2, [Validators.required, Validators.min(0)]],
    minimumStopPercent: [0.5, [Validators.required, Validators.min(0)]],
    maximumStopPercent: [1.5, [Validators.required, Validators.min(0)]],
    allowLong: [true],

    trailingStopAtrMultiplier: [0.6, [Validators.required, Validators.min(0)]],
    trailingProfitRetentionPercent: [70, [Validators.required, Validators.min(0), Validators.max(100)]],

    ignoreMarketHours: [false],
    marketOpenTime: ['09:15:00', Validators.required],
    marketCloseTime: ['15:30:00', Validators.required],
    autoSquareOff: [true],
    paperTradingBalance: [100000, [Validators.required, Validators.min(0)]],
  });

  ngOnInit(): void {
    this.loadConfiguration();
  }

  loadConfiguration(): void {
    this.loading = true;

    this.#angel.getTradingConfiguration()
      .pipe(finalize(() => this.loading = false))
      .subscribe({
        next: (config: TradingConfiguration) => {
          this.currentConfiguration = config ?? null;

          const core = config?.coreStrategy ?? {
            minimumCompletedCandles: 4,
            minimumRecoveryScore: 60,
            minimumPriceChangePercent: 0.05,
            minimumBreakoutStrength: 5,
          };

          const equity = config?.equity ?? {
            exchange: 'NSE',
            productType: 'INTRADAY',
            orderType: 'MARKET',
            duration: 'DAY',
            atrStopMultiplier: 1.2,
            minimumStopPercent: 0.5,
            maximumStopPercent: 1.5,
            allowLong: true,
            exitOrderTimeoutSeconds: 10,
            maxMarketDataAgeSeconds: 15,
            maximumExitRetries: 5,
            maximumOpenPositions: 3,
          };

          const exit = config?.exit ?? {
            trailingStopAtrMultiplier: 0.6,
            trailingProfitRetentionPercent: 70,
          };

          this.form.patchValue({
            enableAutoTrading: config?.enableAutoTrading ?? false,
            paperTrading: config?.paperTrading ?? true,
            enableNotification: config?.enableNotification ?? true,

            minimumCompletedCandles: core.minimumCompletedCandles,
            minimumRecoveryScore: core.minimumRecoveryScore,
            minimumPriceChangePercent: core.minimumPriceChangePercent,
            minimumBreakoutStrength: core.minimumBreakoutStrength,

            riskPercentage: config?.riskPercentage ?? 2,
            maxCapitalPerTradePercent: config?.maxCapitalPerTradePercent ?? 10,
            maximumTotalOpenRisk: config?.maximumTotalOpenRisk ?? 10000,

            exchange: equity.exchange,
            productType: equity.productType,
            orderType: equity.orderType,
            duration: equity.duration,
            atrStopMultiplier: equity.atrStopMultiplier,
            minimumStopPercent: equity.minimumStopPercent,
            maximumStopPercent: equity.maximumStopPercent,
            allowLong: equity.allowLong,

            trailingStopAtrMultiplier: exit.trailingStopAtrMultiplier,
            trailingProfitRetentionPercent: exit.trailingProfitRetentionPercent,

            ignoreMarketHours: config?.ignoreMarketHours ?? false,
            marketOpenTime: this.time(config?.marketOpenTime, '09:15:00'),
            marketCloseTime: this.time(config?.marketCloseTime, '15:30:00'),
            autoSquareOff: config?.autoSquareOff ?? true,
            paperTradingBalance: config?.paperTradingBalance ?? 100000,
          }, { emitEvent: false });

          this.form.markAsPristine();
        },
        error: () => this.#toast.error('Unable to load trading configuration'),
      });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving = true;
    const v = this.form.getRawValue();
    const current = this.currentConfiguration ?? ({} as TradingConfiguration);

    const configuration: TradingConfiguration = {
      ...current,
      id: current.id ?? 'DEFAULT',

      enableAutoTrading: !!v.enableAutoTrading,
      paperTrading: !!v.paperTrading,
      enableNotification: !!v.enableNotification,

      coreStrategy: {
        ...(current.coreStrategy ?? {}),
        minimumCompletedCandles: Number(v.minimumCompletedCandles),
        minimumRecoveryScore: Number(v.minimumRecoveryScore),
        minimumPriceChangePercent: Number(v.minimumPriceChangePercent),
        minimumBreakoutStrength: Number(v.minimumBreakoutStrength),
      },

      riskPercentage: Number(v.riskPercentage),
      maxCapitalPerTradePercent: Number(v.maxCapitalPerTradePercent),
      maximumTotalOpenRisk: Number(v.maximumTotalOpenRisk),

      ignoreMarketHours: !!v.ignoreMarketHours,
      marketOpenTime: this.toTimeSpan(v.marketOpenTime),
      marketCloseTime: this.toTimeSpan(v.marketCloseTime),
      autoSquareOff: !!v.autoSquareOff,
      paperTradingBalance: Number(v.paperTradingBalance),

      equity: {
        ...(current.equity ?? {}),
        exchange: String(v.exchange),
        productType: String(v.productType),
        orderType: String(v.orderType),
        duration: String(v.duration),
        atrStopMultiplier: Number(v.atrStopMultiplier),
        minimumStopPercent: Number(v.minimumStopPercent),
        maximumStopPercent: Number(v.maximumStopPercent),
        allowLong: !!v.allowLong,
      },

      exit: {
        ...(current.exit ?? {}),
        trailingStopAtrMultiplier: Number(v.trailingStopAtrMultiplier),
        trailingProfitRetentionPercent: Number(v.trailingProfitRetentionPercent),
      },
    };

    this.#angel.saveTradingConfiguration(configuration)
      .pipe(finalize(() => this.saving = false))
      .subscribe({
        next: (saved: TradingConfiguration) => {
          this.currentConfiguration = saved ?? configuration;
          this.form.markAsPristine();
          this.#toast.success('Trading configuration saved');
        },
        error: () => this.#toast.error('Unable to save trading configuration'),
      });
  }

  reset(): void {
    this.loadConfiguration();
  }

  private time(value: string | undefined, fallback: string): string {
    if (!value) return fallback;
    return value.length >= 8 ? value.substring(0, 8) : `${value}:00`;
  }

  private toTimeSpan(value: string | null | undefined): string {
    const v = String(value ?? '').trim();
    return v.length === 5 ? `${v}:00` : (v || '00:00:00');
  }
}
