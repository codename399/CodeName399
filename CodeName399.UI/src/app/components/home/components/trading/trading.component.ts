import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AngelOneService } from '../../services/angel-one.service';
import { MarketService } from '../../services/market.service';
import { Gainer } from '../../models/gainer';

type Candle = { timestamp: string; open: number; high: number; low: number; close: number; volume: number; ema9?: number; ema21?: number; crossover?: 'up' | 'down' | null };
type PlotPoint = { x: number; y: number; price: number; timestamp: string; candle: Candle };
type CandleInterval = 'ONE_MINUTE' | 'FIVE_MINUTE' | 'THIRTY_MINUTE';
type ChartRepresentation = 'candles' | 'line';

@Component({
  selector: 'app-trading',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './trading.component.html',
  styleUrl: './trading.component.css',
})
export class TradingComponent implements OnInit, OnDestroy {
  readonly #route = inject(ActivatedRoute);
  readonly #router = inject(Router);
  readonly #angel = inject(AngelOneService);
  readonly #market = inject(MarketService);
  #marketSubscriptions: Subscription[] = [];
  readonly symbolToken = signal('');
  readonly symbol = signal('');
  readonly exchange = signal('NSE');
  readonly candles = signal<Candle[]>([]);
  readonly candleInterval = signal<CandleInterval>('ONE_MINUTE');
  readonly chartRepresentation = signal<ChartRepresentation>('candles');
  readonly loading = signal(true);
  readonly error = signal('');
  readonly stockDetails = signal<Gainer | null>(null);
  readonly actionMessage = signal('');
  readonly actionBusy = signal(false);
  readonly crossoverNotice = signal('');
  readonly selected = signal<Candle | null>(null);
  readonly showPrice = signal(true);
  readonly showEma9 = signal(true);
  readonly showEma21 = signal(true);
  readonly showVolume = signal(false);
  readonly lastPrice = computed(() => this.candles().at(-1)?.close ?? 0);
  readonly plot = computed(() => this.makePlot(this.candles()));
  readonly pricePolyline = computed(() => this.plot().map(p => `${p.x},${p.y}`).join(' '));
  readonly chartBounds = computed(() => { const rows = this.candles(); const values = rows.flatMap(c => [c.low, c.high, ...(this.showEma9() && c.ema9 != null ? [c.ema9] : []), ...(this.showEma21() && c.ema21 != null ? [c.ema21] : [])]); const min = values.length ? Math.min(...values) : 0; const max = values.length ? Math.max(...values) : 1; return { min, max, range: Math.max(max - min, 0.01) }; });
  readonly ema9Polyline = computed(() => this.makeIndicatorPolyline('ema9'));
  readonly ema21Polyline = computed(() => this.makeIndicatorPolyline('ema21'));
  readonly priceAxis = computed(() => {
    const rows = this.candles();
    if (!rows.length) return [] as Array<{ y: number; price: number }>;
    const min = Math.min(...rows.map(c => c.low)); const max = Math.max(...rows.map(c => c.high));
    return Array.from({ length: 5 }, (_, i) => ({ y: 20 + i * 85, price: max - (max - min) * i / 4 }));
  });
  #lastCrossoverKeys = new Set<string>();
  #crossoverNoticeTimer?: ReturnType<typeof setTimeout>;

  ngOnInit(): void {
    this.symbolToken.set(this.#route.snapshot.paramMap.get('symbolToken') ?? '');
    this.symbol.set(this.#route.snapshot.queryParamMap.get('symbol') ?? this.symbolToken());
    this.exchange.set(this.#route.snapshot.queryParamMap.get('exchange') ?? 'NSE');
    if (!this.symbolToken()) { this.error.set('A stock token is required.'); this.loading.set(false); return; }
    this.#angel.getTradingConfiguration().subscribe({ error: () => {} });
    this.#angel.getDashboardSummary().subscribe({ error: () => {} });
    this.loadCandles();

    // Use the same live SignalR market feed as the Angel One dashboard.
    // Subscribe to the selected instrument so its ticks reach this chart even
    // when it is not present on the currently displayed gainer page.
    this.#marketSubscriptions.push(this.#market.gainers$.subscribe(rows => {
      const found = (rows ?? []).find(row => String(row?.symbolToken ?? '') === this.symbolToken());
      if (found) this.stockDetails.set(found);
    }));
    this.#marketSubscriptions.push(this.#market.ticks$.subscribe(tick => {
      if (String(tick?.symbolToken ?? '') !== this.symbolToken()) return;
      const price = Number(tick?.currentPrice ?? tick?.ltp ?? tick?.lastTradedPrice ?? tick?.close ?? 0);
      if (!(price > 0)) return;
      const timestamp = tick?.timestamp ?? tick?.exchangeTimestamp ?? tick?.lastTradeTime ?? new Date().toISOString();
      const volume = Number(tick?.tickDayVolume ?? tick?.volume ?? tick?.dayVolume ?? 0);
      this.applyTick(price, String(timestamp), volume);
      const stock = this.stockDetails();
      if (stock) this.stockDetails.set({ ...stock, currentPrice: price, tickDayVolume: volume });
    }));
    void this.#market.startConnection();
    this.#angel.subscribeStock(this.symbolToken(), this.exchange()).subscribe({
      error: (error: any) => console.error('Unable to subscribe selected stock to live market ticks.', error)
    });
  }

  ngOnDestroy(): void {
    this.#marketSubscriptions.forEach(subscription => subscription.unsubscribe());
    if (this.#crossoverNoticeTimer) clearTimeout(this.#crossoverNoticeTimer);
  }

  loadCandles(): void {
    this.loading.set(true);
    this.#angel.getTodayCandles(this.symbolToken(), this.exchange(), this.candleInterval()).subscribe({
      next: (rows: Array<Candle>) => { this.candles.set(this.withIndicators((rows ?? []).map((c: Candle) => ({ ...c })))); this.loading.set(false); this.error.set(''); },
      error: (e: any) => { this.error.set(e?.error?.message ?? e?.message ?? 'Unable to load today’s candles. Live ticks will still be displayed when available.'); this.loading.set(false); }
    });
  }

  goBack(): void { this.#router.navigate(['/home/angel-one']); }

  hasSufficientFunds(stock: Gainer): boolean {
    if (stock.sufficientFunds != null) return stock.sufficientFunds;
    const investment = Number(stock.minimumInvestmentWithCharges ?? 0);
    return Number(stock.minimumBuyQuantity ?? 0) > 0 && investment > 0 && this.#angel.availableCash() >= investment;
  }

  hasBudgetForMaximum(stock: Gainer): boolean {
    const availableCash = Math.max(0, Number(this.#angel.availableCash() ?? 0));
    return stock.currentPrice > 0 && availableCash >= stock.currentPrice;
  }

  displayNetProfitLoss(stock: Gainer): string {
    const charges = Number(stock.ownedEstimatedCharges ?? 0);
    if (charges <= 0) return 'Charge estimate pending';
    const net = Number(stock.profitLoss ?? 0) - charges;
    return `₹${new Intl.NumberFormat('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(net)}`;
  }

  isNetProfitPositive(stock: Gainer): boolean {
    const charges = Number(stock.ownedEstimatedCharges ?? 0);
    return charges > 0 && Number(stock.profitLoss ?? 0) - charges >= 0;
  }

  isMarketOpenForOrders(): boolean {
    if (this.#angel.configuration()?.ignoreMarketHours) return true;
    const now = new Date();
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Kolkata', year: 'numeric', month: '2-digit', day: '2-digit', weekday: 'short', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(now);
    const get = (type: string) => parts.find(p => p.type === type)?.value ?? '';
    const weekday = get('weekday');
    const dateKey = `${get('year')}-${get('month')}-${get('day')}`;
    const config = this.#angel.configuration();
    const parseMinutes = (value: string | undefined, fallback: string): number => {
      const pieces = String(value ?? fallback).split(':').map(Number);
      return (pieces[0] || 0) * 60 + (pieces[1] || 0);
    };
    const minutes = Number(get('hour')) * 60 + Number(get('minute'));
    const open = parseMinutes(config?.marketOpenTime, '09:15:00');
    const close = parseMinutes(config?.marketCloseTime, '15:00:00');
    const holidays = config?.tradingHolidays ?? [];
    return !['Sat', 'Sun'].includes(weekday) && !holidays.includes(dateKey) && minutes >= open && minutes < close;
  }
  buy(mode: 'minimum' | 'maximum'): void {
    if (!this.isMarketOpenForOrders() || this.actionBusy()) return;
    this.actionBusy.set(true); this.actionMessage.set('');
    this.#angel.buyStock(this.symbolToken(), 1, mode, this.exchange()).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.actionMessage.set(`${mode === 'minimum' ? 'Minimum' : 'Maximum'} buy request submitted.`);
        this.#angel.getDashboardSummary().subscribe({ error: () => {} });
      },
      error: (e: any) => { this.actionBusy.set(false); this.actionMessage.set(e?.error?.message ?? e?.message ?? 'Buy request failed.'); }
    });
  }
  sell(): void {
    const stock = this.stockDetails();
    if (!stock?.isOwned || !this.isMarketOpenForOrders() || this.actionBusy()) return;
    this.actionBusy.set(true); this.actionMessage.set('');
    this.#angel.sellStock(this.symbolToken(), Number(stock.quantity ?? 0), this.exchange()).subscribe({
      next: () => {
        this.actionBusy.set(false);
        this.actionMessage.set('Sell request submitted; live orders close after broker confirmation.');
        this.#angel.getDashboardSummary().subscribe({ error: () => {} });
      },
      error: (e: any) => { this.actionBusy.set(false); this.actionMessage.set(e?.error?.message ?? e?.message ?? 'Sell request failed.'); }
    });
  }
  toggleIndicator(name: 'price' | 'ema9' | 'ema21' | 'volume'): void {
    if (name === 'price') this.showPrice.update(v => !v);
    if (name === 'ema9') this.showEma9.update(v => !v);
    if (name === 'ema21') this.showEma21.update(v => !v);
    if (name === 'volume') this.showVolume.update(v => !v);
  }
  selectPoint(point: PlotPoint): void { this.selected.set(point.candle); }
  trackPoint(_index: number, point: PlotPoint): string { return `${point.timestamp}-${point.price}`; }

  private applyTick(price: number, timestamp: string, volume: number): void {
    const list = [...this.candles()];
    const key = this.intervalKey(timestamp);
    const last = list.at(-1);
    if (last && this.intervalKey(last.timestamp) === key) {
      last.high = Math.max(last.high, price); last.low = Math.min(last.low, price); last.close = price; last.volume = Math.max(last.volume, volume);
      this.candles.set(this.withIndicators(list.slice(-500)));
      return;
    }

    // A new interval means the previous candle is now closed. Only act on a
    // bullish crossover confirmed by that completed candle, never on intrabar noise.
    const completed = list.length ? this.withIndicators(list).at(-1) : undefined;
    list.push({ timestamp: `${key}:00+05:30`, open: price, high: price, low: price, close: price, volume });
    const updated = this.withIndicators(list.slice(-500));
    this.candles.set(updated);
    if (completed?.crossover) this.notifyCrossover(completed, true);
  }

  private intervalKey(value: string): string {
    const minuteKey = this.indiaMinuteKey(value);
    const match = minuteKey.match(/^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2})$/);
    if (!match) return minuteKey;
    const interval = this.candleInterval() === 'THIRTY_MINUTE' ? 30 : this.candleInterval() === 'FIVE_MINUTE' ? 5 : 1;
    const minute = Number(match[3]);
    const bucket = Math.floor(minute / interval) * interval;
    return `${match[1]}T${match[2]}:${String(bucket).padStart(2, '0')}`;
  }

  setCandleInterval(interval: CandleInterval): void {
    if (this.candleInterval() === interval) return;
    this.candleInterval.set(interval);
    this.#lastCrossoverKeys.clear();
    this.loadCandles();
  }

  setChartRepresentation(representation: ChartRepresentation): void { this.chartRepresentation.set(representation); }

  bodyY(candle: Candle): number { return Math.min(this.priceY(candle.open), this.priceY(candle.close)); }
  bodyHeight(candle: Candle): number { return Math.max(1, Math.abs(this.priceY(candle.open) - this.priceY(candle.close))); }

  priceY(price: number): number {
    const bounds = this.chartBounds();
    return 20 + (bounds.max - price) / bounds.range * 340;
  }

  private indiaMinuteKey(value: string): string {
    const input = String(value ?? '').trim();
    // Historical candle timestamps are often timezone-naive and represent IST.
    const naive = input.match(/^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::\d{2}(?:\.\d+)?)?$/);
    if (naive) return `${naive[1]}-${naive[2]}-${naive[3]}T${naive[4]}:${naive[5]}`;

    const parsed = new Date(input);
    if (Number.isNaN(parsed.getTime())) {
      const now = new Date();
      return this.formatIndiaMinute(now);
    }
    return this.formatIndiaMinute(parsed);
  }

  private formatIndiaMinute(value: Date): string {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: 'Asia/Kolkata', year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
    }).formatToParts(value);
    const part = (type: string) => parts.find(item => item.type === type)?.value ?? '00';
    return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`;
  }

  private withIndicators(rows: Candle[]): Candle[] {
    const result = rows.sort((a,b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime());
    let ema9: number | undefined; let ema21: number | undefined; let previousDiff: number | undefined;
    for (const candle of result) {
      // Keep the EMA values used by crossover detection and the UI at a consistent
      // six-decimal precision. Round the calculated EMA values before comparing.
      ema9 = ema9 === undefined ? candle.close : candle.close * (2/10) + ema9 * (1-2/10);
      ema21 = ema21 === undefined ? candle.close : candle.close * (2/22) + ema21 * (1-2/22);
      ema9 = Number(ema9.toFixed(6));
      ema21 = Number(ema21.toFixed(6));
      candle.ema9 = ema9; candle.ema21 = ema21;
      const diff = Number((ema9 - ema21).toFixed(6));
      candle.crossover = previousDiff !== undefined && previousDiff <= 0 && diff > 0 ? 'up' : previousDiff !== undefined && previousDiff >= 0 && diff < 0 ? 'down' : null;
      previousDiff = diff;
    }
    return [...result];
  }


  private notifyCrossover(candle: Candle, candleClosed = false): void {
    // Crossover notifications are controlled by the existing global notification flag.
    if (!this.#angel.configuration()?.enableNotification || !candle.crossover) return;

    const minute = this.indiaMinuteKey(candle.timestamp);
    const direction = candle.crossover;
    const key = `${this.symbolToken()}|${minute}|${direction}`;
    if (this.#lastCrossoverKeys.has(key)) return;
    this.#lastCrossoverKeys.add(key);
    if (direction === 'up' && candleClosed) this.sendBullishCrossoverEmail(candle, minute);

    // Keep the de-duplication cache bounded during long-running chart sessions.
    if (this.#lastCrossoverKeys.size > 300) {
      const oldest = this.#lastCrossoverKeys.values().next().value;
      if (oldest) this.#lastCrossoverKeys.delete(oldest);
    }

    const label = direction === 'up' ? 'Bullish EMA crossover' : 'Bearish EMA crossover';
    const timestamp = new Intl.DateTimeFormat('en-IN', {
      timeZone: 'Asia/Kolkata', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23'
    }).format(new Date());
    const message = `${label} · ${this.symbol()} · EMA9 ₹${(candle.ema9 ?? 0).toFixed(6)} / EMA21 ₹${(candle.ema21 ?? 0).toFixed(6)} · ${timestamp} IST`;
    this.crossoverNotice.set(message);
    if (this.#crossoverNoticeTimer) clearTimeout(this.#crossoverNoticeTimer);
    this.#crossoverNoticeTimer = setTimeout(() => this.crossoverNotice.set(''), 10000);

    // Use desktop notifications when the user has already granted permission;
    // the in-app notice works regardless of browser notification permission.
    if (typeof Notification !== 'undefined' && Notification.permission === 'granted') {
      try { new Notification(label, { body: message }); } catch { /* Browser may block notifications in this context. */ }
    }
  }

  private sendBullishCrossoverEmail(candle: Candle, minute: string): void {
    const close = Number(candle.close || 0);
    if (!(close > 0)) return;
    const stock = this.stockDetails();
    // Estimated round-trip charges are used as the conservative charge hurdle.
    // At a 0.1% favorable move, quantity * close * 0.001 should cover that hurdle.
    const charges = Math.max(0, Number(stock?.estimatedCharges ?? stock?.ownedEstimatedCharges ?? 0));
    const quantity = Math.max(1, Math.ceil(charges / (close * 0.001)));
    const istTime = `${minute.replace('T', ' ')} IST`;
    const name = this.symbol() || stock?.symbol || this.symbolToken();
    const ema9 = Number(candle.ema9 ?? 0).toFixed(6);
    const ema21 = Number(candle.ema21 ?? 0).toFixed(6);
    const subject = `EMA9>EMA21 CLOSED | ${name} | Close ₹${close.toFixed(2)} | ${istTime} | Min Qty ${quantity} @0.1% | EMA9 ${ema9} EMA21 ${ema21}`;
    const message = `<h2>Bullish EMA crossover confirmed at candle close</h2><table border="1" cellpadding="6" cellspacing="0">` +
      `<tr><th>Stock</th><td>${this.escapeHtml(name)}</td></tr>` +
      `<tr><th>Closing price</th><td>₹${close.toFixed(2)}</td></tr>` +
      `<tr><th>Closing time</th><td>${this.escapeHtml(istTime)}</td></tr>` +
      `<tr><th>Minimum estimated quantity to cover charges at 0.1% move</th><td>${quantity}</td></tr>` +
      `<tr><th>Estimated charges used</th><td>₹${charges.toFixed(2)}</td></tr>` +
      `<tr><th>EMA 9</th><td>${ema9}</td></tr><tr><th>EMA 21</th><td>${ema21}</td></tr>` +
      `<tr><th>Interval</th><td>${this.candleInterval()}</td></tr></table>`;
    this.#angel.sendMarketEmail(subject, message, true).subscribe({
      error: (error: any) => console.error('Bullish EMA crossover email endpoint failed.', error)
    });
  }

  private escapeHtml(value: string): string {
    return value.replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch] ?? ch));
  }

  private makeIndicatorPolyline(indicator: 'ema9' | 'ema21'): string {
    const rows = this.candles();
    if (!rows.length) return '';
    return this.plot().map(p => `${p.x},${this.priceY(p.candle[indicator] ?? p.price)}`).join(' ');
  }

  volumeBarHeight(candle: Candle): number {
    const maximum = Math.max(1, ...this.candles().map(c => c.volume));
    return Math.max(1, candle.volume / maximum * 28);
  }

  private makePlot(rows: Candle[]): PlotPoint[] {
    if (!rows.length) return [];
    return rows.map((c, i) => ({ x: 40 + i * (920 / Math.max(1, rows.length-1)), y: this.priceY(c.close), price: c.close, timestamp: c.timestamp, candle: c }));
  }
}
