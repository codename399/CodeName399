import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject } from 'rxjs';
import { API_CONSTANTS } from '../../../../injectors/common-injector';
import { TradingOptimizationStatus } from '../models/trading-optimization-status';

@Injectable({
  providedIn: 'root',
})
export class MarketService {
  #apiConstants = inject(API_CONSTANTS);

  private hub?: signalR.HubConnection;
  private isStarting = false;

  // Keep the latest watchlist snapshot so a component that subscribes after
  // the SignalR connection is already established still receives the current data.
  private gainersSubject = new BehaviorSubject<any[]>([]);
  gainers$ = this.gainersSubject.asObservable();

  private optimizationStatusUpdatedSubject = new BehaviorSubject<TradingOptimizationStatus | null>(null);
  optimizationStatusUpdated$ = this.optimizationStatusUpdatedSubject.asObservable();

  async startConnection(): Promise<void> {
    if (this.hub?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    if (this.isStarting) {
      return;
    }

    this.isStarting = true;

    try {
      if (!this.hub) {
        this.hub = new signalR.HubConnectionBuilder()
          .withUrl(
            this.#apiConstants.getUrl(
              this.#apiConstants.marketHub,
              false
            )
          )
          .withAutomaticReconnect()
          .build();

        this.hub.on('GainersUpdated', (data: any[]) => {
          this.gainersSubject.next(data);
        });

        this.hub.on('OptimizationStatusUpdated', (status: TradingOptimizationStatus) => {
          if (status) {
            this.optimizationStatusUpdatedSubject.next(status);
          }
        });

        this.hub.onreconnected(async () => {
          // Recover the authoritative snapshot through SignalR after reconnect.
          // Do not fall back to the REST status endpoint here.
          try {
            const status = await this.hub?.invoke<TradingOptimizationStatus>('GetOptimizationStatus');
            if (status) {
              this.optimizationStatusUpdatedSubject.next(status);
            }
          } catch (err) {
            console.error('Failed to recover optimization status after SignalR reconnect.', err);
          }
        });
      }

      await this.hub.start();
      console.log('SignalR Connected');
    } catch (err) {
      console.error(err);
    } finally {
      this.isStarting = false;
    }
  }

  async stopConnection(): Promise<void> {
    if (this.hub) {
      await this.hub.stop();
    }
  }
}