import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { tap } from 'rxjs';


import { API_CONSTANTS } from '../../../../injectors/common-injector';


import { DashboardSummary } from '../models/dashboard-summary';

import { Gainer } from '../models/gainer';
import { TradingConfiguration } from '../models/trading-configuration';

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

  get isAutoTradingEnabled(): boolean {
    return this.configuration()?.enableAutoTrading ?? false;
  }

  get selectedStrategy() {
    return this.configuration()?.strategy;
  }

  get riskPercentage(): number {
    return this.configuration()?.riskPercentage ?? 0;
  }

  get maxDailyTrades(): number {
    return this.configuration()?.maxDailyTrades ?? 0;
  }
}
