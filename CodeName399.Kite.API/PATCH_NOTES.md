# Trading strategy patch

## Entry: trend + pullback + recovery
- Requires at least 34 valid completed candles.
- EMA25 must be rising versus three completed candles earlier.
- Latest close must be above EMA25 and session VWAP (both rounded to 6 decimals).
- In the preceding five completed candles, price must have pulled back close to EMA25/VWAP or traded through the support zone.
- Latest candle must be bullish and close above the previous candle high.
- Initial stop is placed one paisa below the five-candle pullback swing low; existing percentage stop is the fallback if that level is invalid relative to the actual entry.

## Exit
- Live ticks continue to enforce initial stop loss, optional target, and optional trailing stop.
- Removed the previous immediate `NET_PROFIT_EXIT` behaviour.
- A completed candle close below session VWAP triggers `VWAP_CLOSE_EXIT`; the actual sell is submitted using the current live tick price.
- VWAP exit is evaluated at most once per symbol per minute.

## Top-10 quote price filter
- Added `MinimumStockPrice` to `TradingConfiguration`, default `20`.
- `GetTopQuotes` now excludes quotes whose LTP is below the configured minimum.
- Added the default to appsettings.json and validation against negative values.

No profitability guarantee is implied; paper-trade and validate over multiple complete sessions before enabling live auto-trading.
