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
  searchTerm = '';
  openSection: string | null = 'trading';
  private currentConfiguration: TradingConfiguration | null = null;

  readonly form = this.#fb.group({
    enableAutoTrading: [false],
    enableNotification: [true],
    autoSquareOff: [true],
    paperTradingBalance: [100000, [Validators.required, Validators.min(0)]],

    marketOpenTime: ['09:15:00', Validators.required],
    marketCloseTime: ['15:30:00', Validators.required],
    marketTimeZoneId: ['Asia/Kolkata', Validators.required],
    tradingHolidays: [''],

    excludedSymbols: ['TATSILV-EQ, AONEGOLD-EQ, ACUTAAS-EQ, IDEA-EQ'],
    maxCandidates: [10, [Validators.required, Validators.min(1)]],
    candleInterval: ['ONE_MINUTE', Validators.required],
    candleBatchSize: [2, [Validators.required, Validators.min(1)]],
    batchDelay: [1, [Validators.required, Validators.min(0)]],
    buyBatchSize: [5, [Validators.required, Validators.min(1)]],
    chargeCoverLowestPriceChangeCandles: [5, [Validators.required, Validators.min(1)]],
    autoSquareOffMinutes: [5, [Validators.required, Validators.min(0)]],

    webSocketHeartbeatSeconds: [10, [Validators.required, Validators.min(1)]],
    webSocketPongTimeoutSeconds: [30, [Validators.required, Validators.min(2)]],
    webSocketRetryInitialSeconds: [10, [Validators.required, Validators.min(1)]],
    webSocketRetryMaxSeconds: [60, [Validators.required, Validators.min(1)]],

    visibleColumns: ['star, symbol, reason, suggestion, stopLoss'],
    researchDataVersion: ['v1', Validators.required],

    rawMarketDataEnabled: [true],
    rawMarketDataStoragePath: ['RawMarketData', Validators.required],
    rawMarketDataMaximumZipSizeMb: [100, [Validators.required, Validators.min(1)]],
    rawMarketDataRollOverAtPercent: [95, [Validators.required, Validators.min(1), Validators.max(100)]],
    rawMarketDataRecordOnlyDuringMarketHours: [true],

    lastDailySummarySent: [{ value: '', disabled: true }],
    instrumentLoadedAt: [{ value: '', disabled: true }],
  });

  ngOnInit(): void {
    this.loadConfiguration();
  }

  loadConfiguration(): void {
    this.loading = true;
    this.#angel.getTradingConfiguration()
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: (config) => {
          this.currentConfiguration = config;
          const raw = config.rawMarketData;
          this.form.patchValue({
            enableAutoTrading: config.enableAutoTrading,
            enableNotification: config.enableNotification,
            autoSquareOff: config.autoSquareOff,
            paperTradingBalance: config.paperTradingBalance,
            marketOpenTime: this.time(config.marketOpenTime),
            marketCloseTime: this.time(config.marketCloseTime),
            marketTimeZoneId: config.marketTimeZoneId,
            tradingHolidays: (config.tradingHolidays ?? []).join(', '),
            excludedSymbols: (config.excludedSymbols ?? []).join(', '),
            maxCandidates: config.maxCandidates,
            candleInterval: config.candleInterval,
            candleBatchSize: config.candleBatchSize,
            batchDelay: config.batchDelay,
            buyBatchSize: config.buyBatchSize,
            chargeCoverLowestPriceChangeCandles: config.chargeCoverLowestPriceChangeCandles,
            autoSquareOffMinutes: config.autoSquareOffMinutes,
            webSocketHeartbeatSeconds: config.webSocketHeartbeatSeconds,
            webSocketPongTimeoutSeconds: config.webSocketPongTimeoutSeconds,
            webSocketRetryInitialSeconds: config.webSocketRetryInitialSeconds,
            webSocketRetryMaxSeconds: config.webSocketRetryMaxSeconds,
            visibleColumns: (config.visibleColumns ?? []).join(', '),
            researchDataVersion: config.researchDataVersion,
            rawMarketDataEnabled: raw?.enabled ?? true,
            rawMarketDataStoragePath: raw?.storagePath ?? 'RawMarketData',
            rawMarketDataMaximumZipSizeMb: raw?.maximumZipSizeMb ?? 100,
            rawMarketDataRollOverAtPercent: raw?.rollOverAtPercent ?? 95,
            rawMarketDataRecordOnlyDuringMarketHours: raw?.recordOnlyDuringMarketHours ?? true,
            lastDailySummarySent: config.lastDailySummarySent?.substring(0, 10) ?? '',
            instrumentLoadedAt: config.instrumentLoadedAt ?? '',
          }, { emitEvent: false });
          this.form.markAsPristine();
        },
        error: () => this.#toast.error('Unable to load trading configuration'),
      });
  }

  save(): void {
    if (this.form.invalid || !this.currentConfiguration) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving = true;
    const v = this.form.getRawValue();
    const current = this.currentConfiguration;

    const configuration: TradingConfiguration = {
      ...current,
      enableAutoTrading: !!v.enableAutoTrading,
      enableNotification: !!v.enableNotification,
      autoSquareOff: !!v.autoSquareOff,
      paperTradingBalance: Number(v.paperTradingBalance),
      marketOpenTime: this.toTimeSpan(v.marketOpenTime),
      marketCloseTime: this.toTimeSpan(v.marketCloseTime),
      marketTimeZoneId: String(v.marketTimeZoneId),
      tradingHolidays: this.csv(v.tradingHolidays),
      excludedSymbols: this.csv(v.excludedSymbols),
      maxCandidates: Number(v.maxCandidates),
      candleInterval: String(v.candleInterval),
      candleBatchSize: Number(v.candleBatchSize),
      batchDelay: Number(v.batchDelay),
      buyBatchSize: Number(v.buyBatchSize),
      chargeCoverLowestPriceChangeCandles: Number(v.chargeCoverLowestPriceChangeCandles),
      autoSquareOffMinutes: Number(v.autoSquareOffMinutes),
      webSocketHeartbeatSeconds: Number(v.webSocketHeartbeatSeconds),
      webSocketPongTimeoutSeconds: Number(v.webSocketPongTimeoutSeconds),
      webSocketRetryInitialSeconds: Number(v.webSocketRetryInitialSeconds),
      webSocketRetryMaxSeconds: Number(v.webSocketRetryMaxSeconds),
      visibleColumns: this.csv(v.visibleColumns),
      researchDataVersion: String(v.researchDataVersion),
      rawMarketData: {
        enabled: !!v.rawMarketDataEnabled,
        storagePath: String(v.rawMarketDataStoragePath),
        maximumZipSizeMb: Number(v.rawMarketDataMaximumZipSizeMb),
        rollOverAtPercent: Number(v.rawMarketDataRollOverAtPercent),
        recordOnlyDuringMarketHours: !!v.rawMarketDataRecordOnlyDuringMarketHours,
      },
    };

    this.#angel.saveTradingConfiguration(configuration)
      .pipe(finalize(() => (this.saving = false)))
      .subscribe({
        next: (saved) => {
          this.currentConfiguration = saved;
          this.form.markAsPristine();
          this.#toast.success('Trading configuration saved');
        },
        error: () => this.#toast.error('Unable to save trading configuration'),
      });
  }

  reset(): void {
    this.loadConfiguration();
  }

  toggleSection(section: string): void {
    this.openSection = this.openSection === section ? null : section;
  }

  sectionMatches(...terms: string[]): boolean {
    const search = this.searchTerm.trim().toLowerCase();
    return !search || terms.some((term) => term.toLowerCase().includes(search));
  }

  downloadJson(): void {
    const json = JSON.stringify(this.form.getRawValue(), null, 2);
    const blob = new Blob([json], { type: 'application/json' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `trading-settings-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(link.href);
  }

  get intradayCutoffTime(): string {
    return this.currentConfiguration?.intradayCutoffTime ?? '';
  }

  private time(value: string | null | undefined): string {
    if (!value) return '00:00:00';
    return value.length >= 8 ? value.substring(0, 8) : `${value}:00`;
  }

  private toTimeSpan(value: string | null | undefined): string {
    const v = String(value ?? '').trim();
    return v.length === 5 ? `${v}:00` : v || '00:00:00';
  }

  private csv(value: string | null | undefined): string[] {
    return String(value ?? '').split(',').map(x => x.trim()).filter(Boolean);
  }
}
