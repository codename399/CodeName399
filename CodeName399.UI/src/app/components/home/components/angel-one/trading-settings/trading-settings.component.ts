import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
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
  readonly #router = inject(Router);

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
    enablePullbackOpportunityNotifications: [false],
    enableNetProfitOpportunityNotifications: [false],
    gainersPageSize: [10, [Validators.required, Validators.min(1), Validators.max(100)]],
    minimumBuyProfitChangePercent: [0.25, [Validators.required, Validators.min(0.01), Validators.max(100)]],

    maxCapitalPerTradePercent: [
      10,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    exchange: ['NSE', Validators.required],
    productType: ['INTRADAY', Validators.required],
    orderType: ['MARKET', Validators.required],
    duration: ['DAY', Validators.required],

    ignoreMarketHours: [false],
    marketOpenTime: ['09:15:00', Validators.required],
    marketCloseTime: ['15:00:00', Validators.required],
    intradayEntryCutoffTime: ['15:00:00', Validators.required],

    watchListRefreshMinutes: [1, [Validators.required, Validators.min(1)]],

    tradingHolidays: [''],
    lastDailySummarySent: [''],
    instrumentLoadedAt: [''],

    enableScripConsentForCashOrders: [true],
    webSocketHeartbeatSeconds: [10, [Validators.required, Validators.min(0)]],
    webSocketPongTimeoutSeconds: [30, [Validators.required, Validators.min(0)]],
    webSocketRetryInitialSeconds: [
      10,
      [Validators.required, Validators.min(0)],
    ],
    webSocketRetryMaxSeconds: [60, [Validators.required, Validators.min(0)]],

    visibleColumns: ['stock, ltp, minimumQuantity, charges, investmentWithCharges, profitLoss, pullback, funds, subscription, action'],

    paperTradingBalance: [100000, [Validators.required, Validators.min(0)]],

    rawMarketDataEnabled: [true],
    rawMarketDataStoragePath: ['RawMarketData', Validators.required],
    rawMarketDataMaximumZipSizeMb: [100, [Validators.required, Validators.min(1)]],
    rawMarketDataRollOverAtPercent: [95, [Validators.required, Validators.min(1), Validators.max(99)]],
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
          const rawMarketData = config?.rawMarketData ?? this.defaultRawMarketData();

          this.form.patchValue(
            {
              enableAutoTrading: config?.enableAutoTrading ?? false,
              paperTrading: config?.paperTrading ?? true,
              paperTradingAsLive: config?.paperTradingAsLive ?? true,
              enableNotification: config?.enableNotification ?? true,
              enablePullbackOpportunityNotifications: config?.enablePullbackOpportunityNotifications ?? false,
              enableNetProfitOpportunityNotifications: config?.enableNetProfitOpportunityNotifications ?? false,
              gainersPageSize: config?.gainersPageSize ?? 10,
              minimumBuyProfitChangePercent: config?.minimumBuyProfitChangePercent ?? 0.25,

              maxCapitalPerTradePercent:
                config?.maxCapitalPerTradePercent ?? 10,
              exchange: equity.exchange,
              productType: equity.productType,
              orderType: equity.orderType,
              duration: equity.duration,

              ignoreMarketHours: config?.ignoreMarketHours ?? false,
              marketOpenTime: this.time(config?.marketOpenTime, '09:15:00'),
              marketCloseTime: this.time(config?.marketCloseTime, '15:00:00'),
              intradayEntryCutoffTime: this.time(
                config?.intradayEntryCutoffTime,
                '15:00:00',
              ),

              watchListRefreshMinutes: config?.watchListRefreshMinutes ?? 1,

              tradingHolidays: (config?.tradingHolidays ?? []).join(', '),
              lastDailySummarySent: this.dateOnly(config?.lastDailySummarySent),
              instrumentLoadedAt: this.dateTime(config?.instrumentLoadedAt),

              enableScripConsentForCashOrders:
                config?.enableScripConsentForCashOrders ?? true,
              webSocketHeartbeatSeconds:
                config?.webSocketHeartbeatSeconds ?? 10,
              webSocketPongTimeoutSeconds:
                config?.webSocketPongTimeoutSeconds ?? 30,
              webSocketRetryInitialSeconds:
                config?.webSocketRetryInitialSeconds ?? 10,
              webSocketRetryMaxSeconds: config?.webSocketRetryMaxSeconds ?? 60,

              visibleColumns: this.sanitizeVisibleColumns(
                config?.visibleColumns ?? [
                  'stock', 'ltp', 'minimumQuantity', 'charges',
                  'investmentWithCharges', 'profitLoss', 'pullback',
                  'funds', 'subscription', 'action',
                ],
              ).join(', '),

              paperTradingBalance: config?.paperTradingBalance ?? 100000,

              rawMarketDataEnabled: rawMarketData.enabled,
              rawMarketDataStoragePath: rawMarketData.storagePath,
              rawMarketDataMaximumZipSizeMb: rawMarketData.maximumZipSizeMb,
              rawMarketDataRollOverAtPercent: rawMarketData.rollOverAtPercent,
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
      enablePullbackOpportunityNotifications: !!v.enablePullbackOpportunityNotifications,
      enableNetProfitOpportunityNotifications: !!v.enableNetProfitOpportunityNotifications,
      gainersPageSize: Number(v.gainersPageSize),
      minimumBuyProfitChangePercent: Number(v.minimumBuyProfitChangePercent),

      maxCapitalPerTradePercent: Number(v.maxCapitalPerTradePercent),
      ignoreMarketHours: !!v.ignoreMarketHours,
      marketOpenTime: this.toTimeSpan(v.marketOpenTime),
      marketCloseTime: this.toTimeSpan(v.marketCloseTime),
      intradayEntryCutoffTime: this.toTimeSpan(v.intradayEntryCutoffTime),

      watchListRefreshMinutes: Number(v.watchListRefreshMinutes),
      tradingHolidays: this.csv(v.tradingHolidays),
      lastDailySummarySent: this.nullIfBlank(v.lastDailySummarySent),
      instrumentLoadedAt:
        current.instrumentLoadedAt ?? this.nullIfBlank(v.instrumentLoadedAt),

      researchDataVersion: current.researchDataVersion ?? 'v1',
      enableScripConsentForCashOrders: !!v.enableScripConsentForCashOrders,
      webSocketHeartbeatSeconds: Number(v.webSocketHeartbeatSeconds),
      webSocketPongTimeoutSeconds: Number(v.webSocketPongTimeoutSeconds),
      webSocketRetryInitialSeconds: Number(v.webSocketRetryInitialSeconds),
      webSocketRetryMaxSeconds: Number(v.webSocketRetryMaxSeconds),

      visibleColumns: this.sanitizeVisibleColumns(this.csv(v.visibleColumns)),

      paperTradingBalance: Number(v.paperTradingBalance),

      rawMarketData: {
        enabled: !!v.rawMarketDataEnabled,
        storagePath: String(v.rawMarketDataStoragePath),
        maximumZipSizeMb: Number(v.rawMarketDataMaximumZipSizeMb),
        rollOverAtPercent: Number(v.rawMarketDataRollOverAtPercent),
      },

      equity: {
        exchange: String(v.exchange),
        productType: String(v.productType),
        orderType: String(v.orderType),
        duration: String(v.duration),
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
          void this.#router.navigate(['/home/dashboard']);
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
    };
  }

  private defaultRawMarketData() {
    return {
      enabled: true,
      storagePath: 'RawMarketData',
      maximumZipSizeMb: 100,
      rollOverAtPercent: 95,
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

  private sanitizeVisibleColumns(columns: string[]): string[] {
    const allowed = new Set([
      'stock', 'ltp', 'minimumQuantity', 'charges',
      'investmentWithCharges', 'profitLoss', 'pullback',
      'funds', 'subscription', 'action',
    ]);
    return ['stock', ...new Set(columns.filter((column) => allowed.has(column) && column !== 'stock'))];
  }

  private nullIfBlank(value: string | null | undefined): string | null {
    const v = String(value ?? '').trim();
    return v || null;
  }
}
