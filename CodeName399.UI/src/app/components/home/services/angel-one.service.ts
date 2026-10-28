import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { Observable, of, throwError, tap } from 'rxjs';


import { API_CONSTANTS } from '../../../../injectors/common-injector';


import { DashboardSummary } from '../models/dashboard-summary';

import { Gainer } from '../models/gainer';
import { TradingConfiguration } from '../models/trading-settings';

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

  // ======================================================
  // Dashboard
  // ======================================================

  getDashboardSummary() {
    return this.#http

      .get<DashboardSummary>(
        this.#api.getUrl(
          this.#api.dashboardSummary,

          true,
        ),
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

  getTradingConfiguration() {
    return this.#http

      .get<TradingConfiguration>(
        this.#api.getUrl(
          this.#api.getConfiguration,

          true,
        ),
      )

      .pipe(
        tap((configuration) => {
          this.configuration.set(configuration);
        }),
      );
  }

  saveTradingConfiguration(configuration: TradingConfiguration) {
    return this.#http

      .put<TradingConfiguration>(
        this.#api.getUrl(
          this.#api.setConfiguration,

          true,
        ),

        configuration,
      )

      .pipe(
        tap((configuration) => {
          this.configuration.set(configuration);
        }),
      );
  }


  /**
   * Historical-candle API is not currently exposed by the backend. The chart
   * continues to build candles from the shared live tick stream.
   */
  getTodayCandles(
    _symbolToken: string,
    _exchange = 'NSE',
    _interval = 'ONE_MINUTE',
  ): Observable<Array<{ timestamp: string; open: number; high: number; low: number; close: number; volume: number }>> {
    return of([]);
  }

  /** The selected instrument uses the shared MarketService SignalR feed. */
  subscribeStock(_symbolToken: string, _exchange = 'NSE'): Observable<void> {
    return of(void 0);
  }

  buyStock(_symbolToken: string, _quantity: number, _mode: 'minimum' | 'maximum', _exchange = 'NSE'): Observable<never> {
    return throwError(() => new Error('Manual buy is unavailable: the API has no manual-order endpoint.'));
  }

  sellStock(_symbolToken: string, _quantity: number, _exchange = 'NSE'): Observable<never> {
    return throwError(() => new Error('Manual sell is unavailable: the API has no manual-order endpoint.'));
  }

  sendMarketEmail(_subject: string, _message: string, _isHtml = true): Observable<never> {
    return throwError(() => new Error('Email sending is unavailable from this API endpoint.'));
  }

  get isAutoTradingEnabled(): boolean {
    return this.configuration()?.enableAutoTrading ?? false;
  }

  get selectedInstrumentType(): string { return 'Equity'; }

  get activeInstrumentSettings() {
    return this.configuration()?.equity;
  }

  get riskPercentage(): number {
    return this.configuration()?.riskPercentage ?? 0;
  }
}
