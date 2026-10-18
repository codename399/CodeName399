import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { AngelOneService } from '../../services/angel-one.service';
import { Gainer } from '../../models/gainer';
import { MarketService } from '../../services/market.service';

type Candle = { timestamp: string; open: number; high: number; low: number; close: number; volume: number; ema9?: number; ema21?: number; crossover?: 'up' | 'down' | null };
type PlotPoint = { x: number; y: number; price: number; timestamp: string; candle: Candle };

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
  readonly symbolToken = signal('');
  readonly symbol = signal('');
  readonly exchange = signal('NSE');
  readonly candles = signal<Candle[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly stockDetails = signal<Gainer | null>(null);
  readonly actionMessage = signal('');
  readonly actionBusy = signal(false);
  readonly selected = signal<Candle | null>(null);
  readonly showPrice = signal(true);
  readonly showEma9 = signal(true);
  readonly showEma21 = signal(true);
  readonly showVolume = signal(false);
  readonly lastPrice = computed(() => this.candles().at(-1)?.close ?? 0);
  readonly plot = computed(() => this.makePlot(this.candles()));
  readonly pricePolyline = computed(() => this.plot().map(p => `${p.x},${p.y}`).join(' '));
  readonly ema9Polyline = computed(() => this.makeIndicatorPolyline('ema9'));
  readonly ema21Polyline = computed(() => this.makeIndicatorPolyline('ema21'));
  readonly priceAxis = computed(() => {
    const rows = this.candles();
    if (!rows.length) return [] as Array<{ y: number; price: number }>;
    const min = Math.min(...rows.map(c => c.low)); const max = Math.max(...rows.map(c => c.high));
    return Array.from({ length: 5 }, (_, i) => ({ y: 20 + i * 85, price: max - (max - min) * i / 4 }));
  });
  #tickSubscription?: Subscription;
  #detailsSubscription?: Subscription;

  ngOnInit(): void {
    this.symbolToken.set(this.#route.snapshot.paramMap.get('symbolToken') ?? '');
    this.symbol.set(this.#route.snapshot.queryParamMap.get('symbol') ?? this.symbolToken());
    this.exchange.set(this.#route.snapshot.queryParamMap.get('exchange') ?? 'NSE');
    if (!this.symbolToken()) { this.error.set('A stock token is required.'); this.loading.set(false); return; }
    this.#market.startConnection();
    this.#angel.getTradingConfiguration().subscribe({ error: () => {} });
    this.#angel.getDashboardSummary().subscribe({ error: () => {} });
    this.#detailsSubscription = this.#market.gainers$.subscribe(rows => {
      const found = (rows ?? []).find(row => String(row?.symbolToken ?? '') === this.symbolToken());
      if (found) this.stockDetails.set(found as Gainer);
    });
    this.loadCandles();
    this.#tickSubscription = this.#market.ticks$.subscribe(tick => {
      if (String(tick?.symbolToken ?? '') !== this.symbolToken()) return;
      const price = Number(tick?.currentPrice ?? tick?.tickLtp ?? tick?.ltp ?? 0);
      if (!(price > 0)) return;
      this.stockDetails.update(previous => previous
        ? { ...previous, ...(tick as Partial<Gainer>), currentPrice: price,
            profitLoss: previous.isOwned ? (price - Number(previous.averagePrice ?? 0)) * Number(previous.quantity ?? 0) : previous.profitLoss,
            currentValue: previous.isOwned ? price * Number(previous.quantity ?? 0) : previous.currentValue }
        : (tick as Gainer));
      const exchangeTime = String(tick?.tickExchangeTime ?? '');
      const lastUpdated = String(tick?.lastTickUpdated ?? '');
      const isUsableTimestamp = (value: string) => value.length > 0 && !value.startsWith('0001-01-01') && !Number.isNaN(new Date(value).getTime());
      const timestamp = isUsableTimestamp(exchangeTime)
        ? exchangeTime
        : isUsableTimestamp(lastUpdated) ? lastUpdated : new Date().toISOString();
      this.applyTick(price, timestamp, Number(tick?.tickDayVolume ?? 0));
    });
  }

  ngOnDestroy(): void { this.#tickSubscription?.unsubscribe(); this.#detailsSubscription?.unsubscribe(); }

  loadCandles(): void {
    this.loading.set(true);
    this.#angel.getTodayCandles(this.symbolToken(), this.exchange()).subscribe({
      next: rows => { this.candles.set(this.withIndicators((rows ?? []).map(c => ({ ...c })))); this.loading.set(false); this.error.set(''); },
      error: e => { this.error.set(e?.error?.message ?? 'Unable to load today’s candles. Live ticks will still be displayed when available.'); this.loading.set(false); }
    });
  }

  goBack(): void { this.#router.navigate(['/home/angel-one']); }

  hasSufficientFunds(stock: Gainer): boolean {
    if (stock.sufficientFunds != null) return stock.sufficientFunds;
    const investment = Number(stock.minimumInvestmentWithCharges ?? 0);
    return Number(stock.minimumBuyQuantity ?? 0) > 0 && investment > 0 && this.#angel.availableCash() >= investment;
  }

  hasBudgetForMaximum(stock: Gainer): boolean {
    const configuration = this.#angel.configuration();
    const allocation = Math.min(100, Math.max(0, Number(configuration?.maxCapitalPerTradePercent ?? 0)));
    const budget = this.#angel.availableCash() * allocation / 100;
    return stock.currentPrice > 0 && budget >= stock.currentPrice;
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
      error: e => { this.actionBusy.set(false); this.actionMessage.set(e?.error?.message ?? 'Buy request failed.'); }
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
      error: e => { this.actionBusy.set(false); this.actionMessage.set(e?.error?.message ?? 'Sell request failed.'); }
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
    const key = this.indiaMinuteKey(timestamp);
    const last = list.at(-1);
    // Historical API timestamps may omit a timezone while live ticks include
    // +05:30 or Z. Compare normalized IST minute keys instead of raw strings,
    // otherwise every live tick can accidentally create a separate candle.
    if (last && this.indiaMinuteKey(last.timestamp) === key) {
      last.high = Math.max(last.high, price); last.low = Math.min(last.low, price); last.close = price; last.volume = Math.max(last.volume, volume);
    } else {
      list.push({ timestamp: `${key}:00+05:30`, open: price, high: price, low: price, close: price, volume });
    }
    this.candles.set(this.withIndicators(list.slice(-500)));
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
      ema9 = ema9 === undefined ? candle.close : candle.close * (2/10) + ema9 * (1-2/10);
      ema21 = ema21 === undefined ? candle.close : candle.close * (2/22) + ema21 * (1-2/22);
      candle.ema9 = ema9; candle.ema21 = ema21;
      const diff = ema9 - ema21;
      candle.crossover = previousDiff !== undefined && previousDiff <= 0 && diff > 0 ? 'up' : previousDiff !== undefined && previousDiff >= 0 && diff < 0 ? 'down' : null;
      previousDiff = diff;
    }
    return [...result];
  }

  private makeIndicatorPolyline(indicator: 'ema9' | 'ema21'): string {
    const rows = this.candles();
    if (!rows.length) return '';
    const prices = rows.flatMap(c => [c.low, c.high]);
    const min = Math.min(...prices); const max = Math.max(...prices); const range = Math.max(max-min, 0.01);
    return this.plot().map(p => `${p.x},${20 + (max - (p.candle[indicator] ?? p.price)) / range * 340}`).join(' ');
  }

  volumeBarHeight(candle: Candle): number {
    const maximum = Math.max(1, ...this.candles().map(c => c.volume));
    return Math.max(1, candle.volume / maximum * 28);
  }

  private makePlot(rows: Candle[]): PlotPoint[] {
    if (!rows.length) return [];
    const prices = rows.flatMap(c => [c.low, c.high, ...(this.showEma9() && c.ema9 ? [c.ema9] : []), ...(this.showEma21() && c.ema21 ? [c.ema21] : [])]);
    const min = Math.min(...prices); const max = Math.max(...prices); const range = Math.max(max-min, 0.01);
    return rows.map((c, i) => ({ x: 40 + i * (920 / Math.max(1, rows.length-1)), y: 20 + (max-c.close) / range * 340, price: c.close, timestamp: c.timestamp, candle: c }));
  }
}
