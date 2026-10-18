import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { tap } from 'rxjs';


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


  subscribeStock(symbolToken: string, exchange = 'NSE') {
    const params = new URLSearchParams({ exchange });
    return this.#http.post(this.#api.getUrl(`/api/market/${encodeURIComponent(symbolToken)}/subscribe?${params.toString()}`, false), {});
  }

  unsubscribeStock(symbolToken: string, exchange = 'NSE') {
    const params = new URLSearchParams({ exchange });
    return this.#http.delete(this.#api.getUrl(`/api/market/${encodeURIComponent(symbolToken)}/subscribe?${params.toString()}`, false));
  }

  getTodayCandles(symbolToken: string, exchange = 'NSE', interval = 'ONE_MINUTE') {
    const params = new URLSearchParams({ exchange, interval });
    return this.#http.get<Array<{ timestamp: string; open: number; high: number; low: number; close: number; volume: number }>>(
      this.#api.getUrl(`/api/market/${encodeURIComponent(symbolToken)}/candles?${params.toString()}`, false),
    );
  }

  getGainersPage(page: number, pageSize: number, search: string) {
    const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (search.trim()) params.set('search', search.trim());
    return this.#http.get<{ page: number; pageSize: number; totalCount: number; totalPages: number; items: Gainer[] }>(
      this.#api.getUrl(`/api/market/gainers?${params.toString()}`, false),
    );
  }

  searchGainers(query: string, page: number, pageSize: number) {
    const params = new URLSearchParams({ query: query.trim(), page: String(page), pageSize: String(pageSize) });
    return this.#http.get<{ page: number; pageSize: number; totalCount: number; totalPages: number; items: Gainer[] }>(
      this.#api.getUrl(`/api/market/search?${params.toString()}`, false),
    );
  }

  buyStock(symbolToken: string, quantity: number, buyMode: 'minimum' | 'maximum' = 'minimum', exchange = 'NSE') {
    return this.#http.post(this.#api.getUrl('/api/market/buy', false), { symbolToken, quantity, buyMode, exchange });
  }

  sellStock(symbolToken: string, quantity: number, exchange = 'NSE') {
    return this.#http.post(this.#api.getUrl('/api/market/sell', false), { symbolToken, quantity, exchange });
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
