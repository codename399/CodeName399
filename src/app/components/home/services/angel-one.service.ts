import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { tap } from 'rxjs';

import { Constants } from '../../../../constants';

import { API_CONSTANTS } from '../../../../injectors/common-injector';

import { AngelOneLoginData } from '../models/angel-one-login-response';

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

  // ======================================================
  // Market Scanner
  // ======================================================

  getTopGainers() {
    return this.#http

      .get<Gainer[]>(
        this.#api.getUrl(
          this.#api.gainers,

          true,
        ),
      )

      .pipe(
        tap((gainers) => {
          this.gainers.set(gainers);
        }),
      );
  }

  // ======================================================
  // Wallet
  // ======================================================

  getAvailableCash() {
    return this.#http.get<number>(
      this.#api.getUrl(
        this.#api.getAvailableCash,

        true,
      ),
    );
  }

  // ======================================================
  // Holdings
  // ======================================================

  getOwnedHoldings() {
    return this.#http.get<string[]>(
      this.#api.getUrl(
        this.#api.ownedHoldings,

        true,
      ),
    );
  }

  // ======================================================
  // Helpers
  // ======================================================

  reloadConfiguration() {
    return this.getTradingConfiguration();
  }

  reloadDashboard() {
    return this.getDashboardSummary();
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

  get scanIntervalSeconds(): number {
    return this.configuration()?.scanIntervalSeconds ?? 0;
  }
}
