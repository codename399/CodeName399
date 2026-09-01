import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { tap } from 'rxjs';


import { API_CONSTANTS } from '../../../../injectors/common-injector';


import { DashboardSummary } from '../models/dashboard-summary';

import { Gainer } from '../models/gainer';
import { TradingConfiguration } from '../models/trading-configuration';
import { TradingOptimizationStatus } from '../models/trading-optimization-status';
import { InstrumentType } from '../models/trading-configuration';

@Injectable({
  providedIn: 'root',
})
export class AngelOneService {
  readonly #http = inject(HttpClient);

  readonly #api = inject(API_CONSTANTS);

  // ======================================================
  // Signals
  // ======================================================

  gainers = signal<Gainer[]>([]);

  availableCash = signal(0);

  configuration = signal<TradingConfiguration | null>(null);

  selectedInstrumentType = signal<InstrumentType>(
    (localStorage.getItem('codename399.instrumentType') as InstrumentType) || 'Equity'
  );

  selectInstrumentType(type: InstrumentType): void {
    this.selectedInstrumentType.set(type);
    localStorage.setItem('codename399.instrumentType', type);
  }

  private tradingPrefix(type: InstrumentType = this.selectedInstrumentType()): string {
    return this.#api.instrumentTradingPrefixes[type] ?? '';
  }

  private tradingUrl(endpoint: string, type: InstrumentType = this.selectedInstrumentType()): string {
    return this.#api.getUrl(`${this.tradingPrefix(type)}${endpoint}`, false);
  }

  // ======================================================
  // Dashboard
  // ======================================================

  getDashboardSummary(type: InstrumentType = this.selectedInstrumentType()) {
    this.selectInstrumentType(type);
    return this.#http

      .get<DashboardSummary>(
        this.tradingUrl(this.#api.dashboardSummary),
      )

      .pipe(
        tap((summary) => {
          this.availableCash.set(summary.availableCash);
        }),
      );
  }

  // ======================================================
  // Trading Configuration
  // ======================================================

  getTradingConfiguration(type: InstrumentType = this.selectedInstrumentType()) {
    this.selectInstrumentType(type);
    return this.#http

      .get<TradingConfiguration>(
        this.tradingUrl(this.#api.getConfiguration),
      )

      .pipe(
        tap((configuration) => {
          this.configuration.set(configuration);
        }),
      );
  }

  saveTradingConfiguration(configuration: TradingConfiguration, type: InstrumentType = this.selectedInstrumentType()) {
    this.selectInstrumentType(type);
    return this.#http

      .put<TradingConfiguration>(
        this.tradingUrl(this.#api.setConfiguration),

        configuration,
      )

      .pipe(
        tap((configuration) => {
          this.configuration.set(configuration);
        }),
      );
  }

  getTradingOptimizationStatus(type: InstrumentType = this.selectedInstrumentType()) {
    this.selectInstrumentType(type);
    return this.#http.get<TradingOptimizationStatus>(
      this.tradingUrl('/api/trading-optimization/status'),
    );
  }

  get isAutoTradingEnabled(): boolean {
    return this.configuration()?.enableAutoTrading ?? false;
  }

  get selectedStrategy() {
    return this.configuration()?.strategy;
  }

  get activeInstrumentSettings() {
    const configuration = this.configuration();
    if (!configuration) return undefined;
    switch (configuration.instrumentType) {
      case 'Futures': return configuration.futures;
      case 'Options': return configuration.options;
      default: return configuration.equity;
    }
  }

  get riskPercentage(): number {
    return this.activeInstrumentSettings?.riskPercentage ?? this.configuration()?.riskPercentage ?? 0;
  }

  get maxDailyTrades(): number {
    const settings = this.activeInstrumentSettings as any;
    return settings?.maximumDailyTrades ?? this.configuration()?.maxDailyTrades ?? 0;
  }
}
