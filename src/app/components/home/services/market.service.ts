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

  // Keep the latest watchlist snapshot so a component that subscribes after
  // the SignalR connection is already established still receives the current data.
  private gainersSubject = new BehaviorSubject<any[]>([]);
  gainers$ = this.gainersSubject.asObservable();

  // Every broker tick is emitted individually. The gainers$ stream remains
  // frame-coalesced for efficient grid rendering, while ticks$ never drops
  // intermediate ticks so UI consumers can process/replay the full feed.
  private readonly tickSubject = new Subject<any>();
  readonly ticks$ = this.tickSubject.asObservable();

  // Tick bursts can contain hundreds/thousands of messages per second.
  // Coalesce them into one UI update per animation frame instead of running
  // Angular change detection and sorting once for every broker tick.
  private readonly pendingTicks = new Map<string, any>();
  private readonly stockIndex = new Map<string, number>();
  private tickFlushScheduled = false;

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
              false,
            ),
          )
          .withAutomaticReconnect()
          .build();

        this.hub.on('GainersUpdated', (data: any[]) => {
          this.pendingTicks.clear();
          this.rebuildStockIndex(data);
          this.gainersSubject.next(data ?? []);
        });

        this.hub.on('StockTickUpdated', (stock: any) => {
          const token = String(stock?.symbolToken ?? '');
          if (!token) return;

          // Never coalesce the event stream itself: emit every received tick
          // before applying the separate visual-grid coalescing strategy.
          this.tickSubject.next(stock);

          // Keep only the newest tick for each stock until the next paint.
          this.pendingTicks.set(token, stock);
          this.scheduleTickFlush();
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

  private rebuildStockIndex(data: any[]): void {
    this.stockIndex.clear();
    for (let index = 0; index < (data?.length ?? 0); index++) {
      const token = String(data[index]?.symbolToken ?? '');
      if (token) {
        this.stockIndex.set(token, index);
      }
    }
  }

  private scheduleTickFlush(): void {
    if (this.tickFlushScheduled) return;
    this.tickFlushScheduled = true;

    const flush = () => {
      this.tickFlushScheduled = false;
      this.flushPendingTicks();
    };

    if (typeof requestAnimationFrame === 'function') {
      requestAnimationFrame(flush);
    } else {
      setTimeout(flush, 16);
    }
  }

  private flushPendingTicks(): void {
    if (this.pendingTicks.size === 0) return;

    const current = this.gainersSubject.value;
    if (current.length === 0) {
      this.pendingTicks.clear();
      return;
    }

    const next = [...current];
    let changed = false;

    for (const [token, stock] of this.pendingTicks) {
      const index = this.stockIndex.get(token);
      if (index === undefined) continue;

      const previous = next[index];
      next[index] = { ...previous, ...stock };
      changed = true;
    }

    this.pendingTicks.clear();

    if (changed) {
      this.gainersSubject.next(next);
    }
  }
}
