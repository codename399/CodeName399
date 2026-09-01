import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject } from 'rxjs';
import { API_CONSTANTS } from '../../../../injectors/common-injector';
import { TradingOptimizationStatus } from '../models/trading-optimization-status';
import { InstrumentType } from '../models/trading-configuration';

@Injectable({ providedIn: 'root' })
export class MarketService {
  #apiConstants = inject(API_CONSTANTS);
  private hub?: signalR.HubConnection;
  private isStarting = false;
  private activeInstrumentType?: InstrumentType;

  private gainersSubject = new BehaviorSubject<any[]>([]);
  gainers$ = this.gainersSubject.asObservable();

  private optimizationStatusUpdatedSubject =
    new BehaviorSubject<TradingOptimizationStatus | null>(null);
  optimizationStatusUpdated$ =
    this.optimizationStatusUpdatedSubject.asObservable();

  private prefix(type: InstrumentType): string {
    return this.#apiConstants.instrumentTradingPrefixes[type] ?? '';
  }

  private hubUrl(type: InstrumentType): string {
    return this.#apiConstants.getUrl(
      `${this.prefix(type)}${this.#apiConstants.marketHub}`,
      false
    );
  }

  async startConnection(type: InstrumentType = 'Equity'): Promise<void> {
    if (
      this.hub?.state === signalR.HubConnectionState.Connected &&
      this.activeInstrumentType === type
    ) return;

    if (this.isStarting) return;
    this.isStarting = true;

    try {
      if (this.hub && this.activeInstrumentType !== type) {
        await this.hub.stop();
        this.hub = undefined;
        this.gainersSubject.next([]);
        this.optimizationStatusUpdatedSubject.next(null);
      }

      if (!this.hub) {
        this.activeInstrumentType = type;
        this.hub = new signalR.HubConnectionBuilder()
          .withUrl(this.hubUrl(type))
          .withAutomaticReconnect()
          .build();

        this.hub.on('GainersUpdated', (data: any[]) => {
          this.gainersSubject.next(data ?? []);
        });

        this.hub.on('StockTickUpdated', (stock: any) => {
          if (!stock?.symbolToken) return;
          const current = this.gainersSubject.value;
          const index = current.findIndex(
            x => String(x?.symbolToken ?? '') === String(stock.symbolToken)
          );
          if (index < 0) return;
          const next = [...current];
          next[index] = { ...next[index], ...stock };
          this.gainersSubject.next(next);
        });

        this.hub.on('OptimizationStatusUpdated',
          (status: TradingOptimizationStatus) => {
            if (status) this.optimizationStatusUpdatedSubject.next(status);
          });
      }

      await this.hub.start();
      console.log(`SignalR Connected: ${type}`);
    } catch (err) {
      console.error(`SignalR connection failed for ${type}`, err);
    } finally {
      this.isStarting = false;
    }
  }

  async stopConnection(): Promise<void> {
    if (this.hub) {
      await this.hub.stop();
      this.hub = undefined;
      this.activeInstrumentType = undefined;
    }
  }
}
