import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { API_CONSTANTS } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class MarketService {
  #apiConstants = inject(API_CONSTANTS);

  private hub?: signalR.HubConnection;
  private isStarting = false;

  private gainersSubject = new Subject<any[]>();
  gainers$ = this.gainersSubject.asObservable();

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