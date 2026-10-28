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
    paperTrading: [true],
    paperTradingAsLive: [true],
    enableNotification: [true],

    riskPercentage: [2, [Validators.required, Validators.min(0)]],
    maxBrokerFailuresBeforeKillSwitch: [
      5,
      [Validators.required, Validators.min(0)],
    ],
    brokerFailureWindowMinutes: [2, [Validators.required, Validators.min(0)]],
    maximumTotalOpenRisk: [10000, [Validators.required, Validators.min(0)]],
    riskReservationSeconds: [10, [Validators.required, Validators.min(0)]],

    exchange: ['NSE', Validators.required],
    productType: ['INTRADAY', Validators.required],
    orderType: ['MARKET', Validators.required],
    duration: ['DAY', Validators.required],
    atrStopMultiplier: [1.2, [Validators.required, Validators.min(0)]],
    minimumStopPercent: [0.5, [Validators.required, Validators.min(0)]],
    maximumStopPercent: [1.5, [Validators.required, Validators.min(0)]],
    allowLong: [true],
    exitOrderTimeoutSeconds: [10, [Validators.required, Validators.min(0)]],
    maxMarketDataAgeSeconds: [15, [Validators.required, Validators.min(0)]],
    maximumExitRetries: [5, [Validators.required, Validators.min(0)]],
    maximumOpenPositions: [3, [Validators.required, Validators.min(0)]],

    trailingStopAtrMultiplier: [0.6, [Validators.required, Validators.min(0)]],
    trailingProfitRetentionPercent: [
      70,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],

    ignoreMarketHours: [false],
    marketOpenTime: ['09:15:00', Validators.required],
    marketCloseTime: ['15:30:00', Validators.required],
    intradayEntryCutoffTime: ['15:00:00', Validators.required],
    equityMisAutoSquareOffTime: ['15:10:00', Validators.required],
    roboAutoSquareOffTime: ['15:05:00', Validators.required],

    casTransitionStart: ['15:15:00', Validators.required],
    casOrderEntryStart: ['15:20:00', Validators.required],
    casMarketOnlyEnd: ['15:25:00', Validators.required],
    casLimitOnlyEnd: ['15:30:00', Validators.required],
    casRandomCloseSafetyCutoff: ['15:28:00', Validators.required],
    casEnd: ['15:40:00', Validators.required],
    casPostCloseEnd: ['16:00:00', Validators.required],
    casPriceBandPercent: [3, [Validators.required, Validators.min(0)]],

    watchListRefreshMinutes: [1, [Validators.required, Validators.min(0)]],
    excludedSymbols: ['TATSILV-EQ, AONEGOLD-EQ, ACUTAAS-EQ, IDEA-EQ'],
    maximumStockPrice: [50, [Validators.required, Validators.min(0)]],
    entryType: ['PullbackRecovery', Validators.required],
    enableSidewaysMarketFilter: [true],
    enableChoppinessIndicator: [true],
    choppinessPeriod: [14, [Validators.required, Validators.min(1)]],
    sidewaysChoppinessThreshold: [61.8, [Validators.required, Validators.min(0), Validators.max(100)]],
    chargeCoverPriceIncrement: [0.01, [Validators.required, Validators.min(0)]],
    stopLossPercent: [0.1, [Validators.required, Validators.min(0)]],
    maximumDiscoveryCandidates: [10, [Validators.required, Validators.min(0)]],
    buyCutoffMinutesBeforeClose: [30, [Validators.required, Validators.min(0)]],

    marketTimeZoneId: ['Asia/Kolkata', Validators.required],
    tradingHolidays: [''],
    lastDailySummarySent: [''],
    instrumentLoadedAt: [''],

    enableEntryExecutionAudit: [true],
    enableScripConsentForCashOrders: [true],
    webSocketHeartbeatSeconds: [10, [Validators.required, Validators.min(0)]],
    webSocketPongTimeoutSeconds: [30, [Validators.required, Validators.min(0)]],
    webSocketRetryInitialSeconds: [
      10,
      [Validators.required, Validators.min(0)],
    ],
    webSocketRetryMaxSeconds: [60, [Validators.required, Validators.min(0)]],
    brokerPositionConfirmationDelaySeconds: [
      1,
      [Validators.required, Validators.min(0)],
    ],
    squareOffRetryDelaySeconds: [1, [Validators.required, Validators.min(0)]],
    stopLossConfirmationSeconds: [2, [Validators.required, Validators.min(0)]],

    buyTradingInterval: [1000, [Validators.required, Validators.min(0)]],
    sellTradingInterval: [1000, [Validators.required, Validators.min(0)]],
    visibleColumns: ['star, symbol, reason, suggestion, stopLoss'],

    autoSquareOff: [true],
    paperTradingBalance: [100000, [Validators.required, Validators.min(0)]],

    researchDataVersion: ['v1', Validators.required],

    rawMarketDataEnabled: [true],
    rawMarketDataStoragePath: ['RawMarketData', Validators.required],
    rawMarketDataMaximumZipSizeMb: [100, [Validators.required, Validators.min(0)]],
    rawMarketDataRollOverAtPercent: [95, [Validators.required, Validators.min(0), Validators.max(100)]],
    rawMarketDataRecordOnlyDuringMarketHours: [true],
  });

  ngOnInit(): void {
    this.loadConfiguration();
  }

  loadConfiguration(): void {
    this.loading = true;

    this.#angel
      .getTradingConfiguration()
      .pipe(finalize(() => (this.loading = false)))
      .subscribe({
        next: (config: TradingConfiguration) => {
          this.currentConfiguration = config ?? null;

          const equity = config?.equity ?? this.defaultEquity();
          const exit = config?.exit ?? this.defaultExit();
          const rawMarketData = config?.rawMarketData ?? this.defaultRawMarketData();

          this.form.patchValue(
            {
              enableAutoTrading: config?.enableAutoTrading ?? false,
              paperTrading: config?.paperTrading ?? true,
              paperTradingAsLive: config?.paperTradingAsLive ?? true,
              enableNotification: config?.enableNotification ?? true,

              riskPercentage: config?.riskPercentage ?? 2,
              maxBrokerFailuresBeforeKillSwitch:
                config?.maxBrokerFailuresBeforeKillSwitch ?? 5,
              brokerFailureWindowMinutes:
                config?.brokerFailureWindowMinutes ?? 2,
              maximumTotalOpenRisk: config?.maximumTotalOpenRisk ?? 10000,
              riskReservationSeconds: config?.riskReservationSeconds ?? 10,

              exchange: equity.exchange,
              productType: equity.productType,
              orderType: equity.orderType,
              duration: equity.duration,
              atrStopMultiplier: equity.atrStopMultiplier,
              minimumStopPercent: equity.minimumStopPercent,
              maximumStopPercent: equity.maximumStopPercent,
              allowLong: equity.allowLong,
              exitOrderTimeoutSeconds: equity.exitOrderTimeoutSeconds,
              maxMarketDataAgeSeconds: equity.maxMarketDataAgeSeconds,
              maximumExitRetries: equity.maximumExitRetries,
              maximumOpenPositions: equity.maximumOpenPositions,

              trailingStopAtrMultiplier: exit.trailingStopAtrMultiplier,
              trailingProfitRetentionPercent:
                exit.trailingProfitRetentionPercent,

              ignoreMarketHours: config?.ignoreMarketHours ?? false,
              marketOpenTime: this.time(config?.marketOpenTime, '09:15:00'),
              marketCloseTime: this.time(config?.marketCloseTime, '15:30:00'),
              intradayEntryCutoffTime: this.time(
                config?.intradayEntryCutoffTime,
                '15:00:00',
              ),
              equityMisAutoSquareOffTime: this.time(
                config?.equityMisAutoSquareOffTime,
                '15:10:00',
              ),
              roboAutoSquareOffTime: this.time(
                config?.roboAutoSquareOffTime,
                '15:05:00',
              ),

              casTransitionStart: this.time(
                config?.casTransitionStart,
                '15:15:00',
              ),
              casOrderEntryStart: this.time(
                config?.casOrderEntryStart,
                '15:20:00',
              ),
              casMarketOnlyEnd: this.time(config?.casMarketOnlyEnd, '15:25:00'),
              casLimitOnlyEnd: this.time(config?.casLimitOnlyEnd, '15:30:00'),
              casRandomCloseSafetyCutoff: this.time(
                config?.casRandomCloseSafetyCutoff,
                '15:28:00',
              ),
              casEnd: this.time(config?.casEnd, '15:40:00'),
              casPostCloseEnd: this.time(config?.casPostCloseEnd, '16:00:00'),
              casPriceBandPercent: config?.casPriceBandPercent ?? 3,

              watchListRefreshMinutes: config?.watchListRefreshMinutes ?? 1,
              excludedSymbols: (config?.excludedSymbols ?? []).join(', '),
              maximumStockPrice: config?.maximumStockPrice ?? 50,
              entryType: config?.entryType ?? 'PullbackRecovery',
              enableSidewaysMarketFilter: config?.enableSidewaysMarketFilter ?? true,
              enableChoppinessIndicator: config?.enableChoppinessIndicator ?? true,
              choppinessPeriod: config?.choppinessPeriod ?? 14,
              sidewaysChoppinessThreshold: config?.sidewaysChoppinessThreshold ?? 61.8,
              chargeCoverPriceIncrement: config?.chargeCoverPriceIncrement ?? 0.01,
              stopLossPercent: config?.stopLossPercent ?? 0.1,
              maximumDiscoveryCandidates: config?.maximumDiscoveryCandidates ?? 10,
              buyCutoffMinutesBeforeClose: config?.buyCutoffMinutesBeforeClose ?? 30,

              marketTimeZoneId: config?.marketTimeZoneId ?? 'Asia/Kolkata',
              tradingHolidays: (config?.tradingHolidays ?? []).join(', '),
              lastDailySummarySent: this.dateOnly(config?.lastDailySummarySent),
              instrumentLoadedAt: this.dateTime(config?.instrumentLoadedAt),

              enableEntryExecutionAudit:
                config?.enableEntryExecutionAudit ?? true,
              enableScripConsentForCashOrders:
                config?.enableScripConsentForCashOrders ?? true,
              webSocketHeartbeatSeconds:
                config?.webSocketHeartbeatSeconds ?? 10,
              webSocketPongTimeoutSeconds:
                config?.webSocketPongTimeoutSeconds ?? 30,
              webSocketRetryInitialSeconds:
                config?.webSocketRetryInitialSeconds ?? 10,
              webSocketRetryMaxSeconds: config?.webSocketRetryMaxSeconds ?? 60,
              brokerPositionConfirmationDelaySeconds:
                config?.brokerPositionConfirmationDelaySeconds ?? 1,
              squareOffRetryDelaySeconds:
                config?.squareOffRetryDelaySeconds ?? 1,
              stopLossConfirmationSeconds:
                config?.stopLossConfirmationSeconds ?? 2,

              buyTradingInterval: config?.buyTradingInterval ?? 1000,
              sellTradingInterval: config?.sellTradingInterval ?? 1000,
              visibleColumns: (
                config?.visibleColumns ?? [
                  'stock',
                  'ltp',
                  'minimumQuantity',
                  'charges',
                  'investmentWithCharges',
                  'profitLoss',
                  'pullback',
                  'funds',
                  'subscription',
                  'action',
                ]
              ).join(', '),

              autoSquareOff: config?.autoSquareOff ?? true,
              paperTradingBalance: config?.paperTradingBalance ?? 100000,

              researchDataVersion: config?.researchDataVersion ?? 'v1',

              rawMarketDataEnabled: rawMarketData.enabled,
              rawMarketDataStoragePath: rawMarketData.storagePath,
              rawMarketDataMaximumZipSizeMb: rawMarketData.maximumZipSizeMb,
              rawMarketDataRollOverAtPercent: rawMarketData.rollOverAtPercent,
              rawMarketDataRecordOnlyDuringMarketHours: rawMarketData.recordOnlyDuringMarketHours,
            },
            { emitEvent: false },
          );

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
      paperTradingAsLive: !!v.paperTradingAsLive,
      enableNotification: !!v.enableNotification,

      riskPercentage: Number(v.riskPercentage),
      maxBrokerFailuresBeforeKillSwitch: Number(
        v.maxBrokerFailuresBeforeKillSwitch,
      ),
      brokerFailureWindowMinutes: Number(v.brokerFailureWindowMinutes),
      maximumTotalOpenRisk: Number(v.maximumTotalOpenRisk),
      riskReservationSeconds: Number(v.riskReservationSeconds),

      ignoreMarketHours: !!v.ignoreMarketHours,
      marketOpenTime: this.toTimeSpan(v.marketOpenTime),
      marketCloseTime: this.toTimeSpan(v.marketCloseTime),
      intradayEntryCutoffTime: this.toTimeSpan(v.intradayEntryCutoffTime),
      equityMisAutoSquareOffTime: this.toTimeSpan(v.equityMisAutoSquareOffTime),
      roboAutoSquareOffTime: this.toTimeSpan(v.roboAutoSquareOffTime),

      casTransitionStart: this.toTimeSpan(v.casTransitionStart),
      casOrderEntryStart: this.toTimeSpan(v.casOrderEntryStart),
      casMarketOnlyEnd: this.toTimeSpan(v.casMarketOnlyEnd),
      casLimitOnlyEnd: this.toTimeSpan(v.casLimitOnlyEnd),
      casRandomCloseSafetyCutoff: this.toTimeSpan(v.casRandomCloseSafetyCutoff),
      casEnd: this.toTimeSpan(v.casEnd),
      casPostCloseEnd: this.toTimeSpan(v.casPostCloseEnd),
      casPriceBandPercent: Number(v.casPriceBandPercent),

      watchListRefreshMinutes: Number(v.watchListRefreshMinutes),
      excludedSymbols: this.csv(v.excludedSymbols),
      maximumStockPrice: Number(v.maximumStockPrice),
      entryType: String(v.entryType),
      enableSidewaysMarketFilter: !!v.enableSidewaysMarketFilter,
      enableChoppinessIndicator: !!v.enableChoppinessIndicator,
      choppinessPeriod: Number(v.choppinessPeriod),
      sidewaysChoppinessThreshold: Number(v.sidewaysChoppinessThreshold),
      chargeCoverPriceIncrement: Number(v.chargeCoverPriceIncrement),
      stopLossPercent: Number(v.stopLossPercent),
      maximumDiscoveryCandidates: Number(v.maximumDiscoveryCandidates),
      buyCutoffMinutesBeforeClose: Number(v.buyCutoffMinutesBeforeClose),

      marketTimeZoneId: String(v.marketTimeZoneId),
      tradingHolidays: this.csv(v.tradingHolidays),
      lastDailySummarySent: this.nullIfBlank(v.lastDailySummarySent),
      instrumentLoadedAt:
        current.instrumentLoadedAt ?? this.nullIfBlank(v.instrumentLoadedAt),

      enableEntryExecutionAudit: !!v.enableEntryExecutionAudit,
      enableScripConsentForCashOrders: !!v.enableScripConsentForCashOrders,
      webSocketHeartbeatSeconds: Number(v.webSocketHeartbeatSeconds),
      webSocketPongTimeoutSeconds: Number(v.webSocketPongTimeoutSeconds),
      webSocketRetryInitialSeconds: Number(v.webSocketRetryInitialSeconds),
      webSocketRetryMaxSeconds: Number(v.webSocketRetryMaxSeconds),
      brokerPositionConfirmationDelaySeconds: Number(
        v.brokerPositionConfirmationDelaySeconds,
      ),
      squareOffRetryDelaySeconds: Number(v.squareOffRetryDelaySeconds),
      stopLossConfirmationSeconds: Number(v.stopLossConfirmationSeconds),

      buyTradingInterval: Number(v.buyTradingInterval),
      sellTradingInterval: Number(v.sellTradingInterval),
      visibleColumns: this.csv(v.visibleColumns),

      autoSquareOff: !!v.autoSquareOff,
      paperTradingBalance: Number(v.paperTradingBalance),

      researchDataVersion: String(v.researchDataVersion),

      rawMarketData: {
        ...(current.rawMarketData ?? this.defaultRawMarketData()),
        enabled: !!v.rawMarketDataEnabled,
        storagePath: String(v.rawMarketDataStoragePath),
        maximumZipSizeMb: Number(v.rawMarketDataMaximumZipSizeMb),
        rollOverAtPercent: Number(v.rawMarketDataRollOverAtPercent),
        recordOnlyDuringMarketHours: !!v.rawMarketDataRecordOnlyDuringMarketHours,
      },

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
        exitOrderTimeoutSeconds: Number(v.exitOrderTimeoutSeconds),
        maxMarketDataAgeSeconds: Number(v.maxMarketDataAgeSeconds),
        maximumExitRetries: Number(v.maximumExitRetries),
        maximumOpenPositions: Number(v.maximumOpenPositions),
      },

      exit: {
        ...(current.exit ?? {}),
        trailingStopAtrMultiplier: Number(v.trailingStopAtrMultiplier),
        trailingProfitRetentionPercent: Number(
          v.trailingProfitRetentionPercent,
        ),
      },
    };

    this.#angel
      .saveTradingConfiguration(configuration)
      .pipe(finalize(() => (this.saving = false)))
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

  toggleSection(section: string): void {
    this.openSection = this.openSection === section ? null : section;
  }

  updateSearch(event: Event): void {
    this.searchTerm = (event.target as HTMLInputElement).value;
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

  private defaultEquity() {
    return {
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
  }

  private defaultExit() {
    return {
      trailingStopAtrMultiplier: 0.6,
      trailingProfitRetentionPercent: 70,
    };
  }

  private defaultRawMarketData() {
    return {
      enabled: true,
      storagePath: 'RawMarketData',
      maximumZipSizeMb: 100,
      rollOverAtPercent: 95,
      recordOnlyDuringMarketHours: true,
    };
  }

  private time(value: string | undefined, fallback: string): string {
    if (!value) return fallback;
    return value.length >= 8 ? value.substring(0, 8) : `${value}:00`;
  }

  private dateOnly(value: string | null | undefined): string {
    return value ? value.substring(0, 10) : '';
  }

  private dateTime(value: string | null | undefined): string {
    return value ? value.substring(0, 19) : '';
  }

  private toTimeSpan(value: string | null | undefined): string {
    const v = String(value ?? '').trim();
    return v.length === 5 ? `${v}:00` : v || '00:00:00';
  }

  private csv(value: string | null | undefined): string[] {
    return String(value ?? '')
      .split(',')
      .map((x) => x.trim())
      .filter(Boolean);
  }

  private nullIfBlank(value: string | null | undefined): string | null {
    const v = String(value ?? '').trim();
    return v || null;
  }
}
