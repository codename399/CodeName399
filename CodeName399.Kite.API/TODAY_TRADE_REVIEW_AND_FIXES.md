# 2026-10-21 Trade Review and Fixes

## What the supplied trade log shows

- Closed trades: **31**
- Gross P&L: **+₹27.79**
- Recorded charges: **₹830.55**
- Net P&L: **-₹802.76**
- EMA-bearish-crossover exits: **17 trades**, **-₹780.44 net**
- Trailing-stop exits: **14 trades**, **-₹22.32 net**

The biggest loss driver in the supplied log was not just entry timing: 17 positions were closed by an EMA9-below-EMA21 event, including trades with material losses. That contradicted the intended exit rule that positions should be managed by the protective stop and charge-aware trailing stop, not by an EMA crossover exit.

Several trailing-stop exits also finished below net break-even. In the code, the stop was evaluated against LTP while the paper sell was priced from Bid for a long position. In a spread or a fast move, LTP can remain above the stop while the executable Bid is already below it. Paper entries also used LTP instead of Ask for long buys, so simulated entry prices could be inconsistent with executable quotes.

One example from the tick archive around UNIONBANK's entry shows LTP at ₹183.15 while Bid was ₹182.99 and Ask was ₹183.02. The log recorded ₹183.15 as the paper entry price. This can overstate simulated entry cost versus the executable Ask at that moment.

## Changes made

1. **Removed EMA bearish crossover as an exit trigger.** Exit is now driven by the protective stop or the charge-aware trailing stop.
2. **Evaluate stop breaches using the executable side of the quote.** Long positions use the freshest websocket TickBid (then quote Bid, then LTP); short positions use TickAsk (then quote Ask, then LTP).
3. **Removed the stop confirmation delay from the actual sell trigger.** A breached protective/trailing stop is acted on immediately rather than waiting for an additional confirmation period that can increase slippage.
4. **Paper entries use executable quote prices.** Long entries use the freshest TickAsk and short entries use TickBid, with quote-side and then LTP fallback only when the relevant side is unavailable. The stop is shifted by the entry-price difference and break-even is recalculated from the paper entry reference.
5. **Recalculate live protective stop from actual broker average fill price.** The bot no longer retains a stop derived from the pre-order LTP when the broker fill differs.
6. **Preserve close-confirmed pullback/recovery signals.** The buy worker now consumes the EMA and indicator values validated on the completed recovery candle instead of rejecting that signal because an unfinished candle changed before the next buy cycle.

## Important limitation

These changes correct identified execution/exit logic defects; they do not guarantee profitable trades. Charges are substantial relative to the small gross gains in this log, and spread/slippage can still make a stop fill below its trigger. Validate the changes in paper mode and compare entry/exit Bid/Ask, estimated charges, and realized P&L before enabling live orders.

## Build status

The source archive was patched and ZIP integrity checked. The .NET SDK is not installed in this environment, so a .NET build/test run could not be performed here.
