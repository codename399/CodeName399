import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { API_CONSTANTS } from '../../../../injectors/common-injector';

@Injectable({
  providedIn: 'root',
})
export class MarketService {
  #apiConstants = inject(API_CONSTANTS);

  private hub?: signalR.HubConnection;
  private isStarting = false;

  private readonly quotesSubject = new BehaviorSubject<any[]>([]);
  readonly quotes$ = this.quotesSubject.asObservable();

  private readonly tickSubject = new Subject<any>();
  readonly ticks$ = this.tickSubject.asObservable();

  private readonly pendingTicks = new Map<string, any>();
  private readonly stockIndex = new Map<string, number>();
  private tickFlushScheduled = false;

  async startConnection(): Promise<void> {
    if (this.hub?.state === signalR.HubConnectionState.Connected || this.isStarting) {
      return;
    }

    this.isStarting = true;
    try {
      if (!this.hub) {
        this.hub = new signalR.HubConnectionBuilder()
          .withUrl(this.#apiConstants.getUrl(this.#apiConstants.marketHub, false))
          .withAutomaticReconnect()
          .build();

        this.hub.on('QuotesUpdated', (data: any[]) => {
          this.pendingTicks.clear();
          const quotes = data ?? [];
          this.rebuildStockIndex(quotes);
          this.quotesSubject.next(quotes);
        });

        this.hub.on('StockTickUpdated', (quote: any) => {
          const token = String(quote?.symbolToken ?? '');
          if (!token) return;
          this.tickSubject.next(quote);
          this.pendingTicks.set(token, quote);
          this.scheduleTickFlush();
        });
      }

      await this.hub.start();
      console.log('Market SignalR connected');
    } catch (error) {
      console.error('Market SignalR connection failed', error);
    } finally {
      this.isStarting = false;
    }
  }

  async stopConnection(): Promise<void> {
    if (this.hub) await this.hub.stop();
  }

  private rebuildStockIndex(quotes: any[]): void {
    this.stockIndex.clear();
    quotes.forEach((quote, index) => {
      const token = String(quote?.symbolToken ?? '');
      if (token) this.stockIndex.set(token, index);
    });
  }

  private scheduleTickFlush(): void {
    if (this.tickFlushScheduled) return;
    this.tickFlushScheduled = true;
    const flush = () => {
      this.tickFlushScheduled = false;
      this.flushPendingTicks();
    };
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(flush);
    else setTimeout(flush, 16);
  }

  private flushPendingTicks(): void {
    if (!this.pendingTicks.size) return;
    const current = this.quotesSubject.value;
    if (!current.length) {
      this.pendingTicks.clear();
      return;
    }

    const next = [...current];
    let changed = false;
    for (const [token, quote] of this.pendingTicks) {
      const index = this.stockIndex.get(token);
      if (index === undefined) continue;
      next[index] = { ...next[index], ...quote };
      changed = true;
    }
    this.pendingTicks.clear();
    if (changed) this.quotesSubject.next(next);
  }
}
