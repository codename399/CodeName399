# CodeName399 Equity Gainers and Manual Trading Requirements

This document is the source-of-truth checklist from `ManualTradingPlan.txt`. Do not add unrelated strategy logic or configuration.

## API requirements

1. Load equity instruments.
2. Fetch quotes.
3. Filter top gainers that are highly active and highly traded.
4. Apply server-side pagination to gainers; default page size is 10 and configurable.
5. Subscribe the filtered stocks.
6. Calculate minimum buy quantity so the expected minimum price move covers charges. Default minimum price change is 0.25%, configurable.
7. Recalculate minimum buy quantity on every tick.
8. Provide a buy endpoint.
9. Provide a sell endpoint.
10. Provide subscribe and unsubscribe endpoints.
11. Provide a send-email endpoint.
12. Provide server-side search over gainers.
13. Preserve paper trading, auto trading, and paper-as-live options.
14. Update Ocelot routes to expose required endpoints and remove only confirmed unwanted routes.
15. Enforce market hours only for buy and sell actions, not discovery or raw-data capture.

## UI requirements

1. Show gainers returned by the API.
2. Use SignalR for live tick updates.
3. Use server-side pagination.
4. Grid columns: stock, LTP, minimum quantity, charges, minimum investment including charges, P/L if owned, buy button if not owned, and sell button if owned.
5. Use icons for action buttons.
6. Provide a search button that searches the server-side gainer list.
7. Show whether the last candle close was a pullback.
8. Sort owned stocks first, pullbacks second, then remaining stocks.
9. Notify when an owned stock has positive net profit and a sell opportunity, or a stock candle closes with a pullback and a buy opportunity exists.
10. Allow manual subscribe and unsubscribe.
11. Sort manually subscribed stocks after owned stocks.
12. Indicate whether sufficient funds are available to buy each stock.
13. Keep buy- and sell-opportunity notifications behind separate feature flags.
14. Hide buy/sell buttons outside market hours.

## Implementation constraints

- Equity instruments only for this screen and flow.
- Preserve the existing paper-trading, auto-trading, and paper-as-live settings.
- Do not introduce unrelated trading strategy rules or configuration.
- Keep changes minimal and explain any requirement that cannot be completed safely from the available project structure.
- Build the .NET solution and Angular application before considering the work complete.

## Changes in this patch

- Added `GET /api/market/gainers` with server-side `page`, `pageSize` (default 10), and `search` query parameters.
- Added subscribe and unsubscribe endpoints for stocks already present in the live watchlist.
- Added a send-email endpoint that uses the existing notification configuration and SMTP helper.
- Added matching Ocelot routes.
- Updated initial SignalR connection payload to send the current watchlist rather than only a narrower actionable-signal subset.

## Not yet verified / outstanding

- The manual buy and sell endpoints and their complete charge-aware validations are not added by this patch; they need to be wired to the existing order execution path rather than duplicating broker order logic.
- The Angular grid and its charge/quantity/funds columns, action icons, market-hours visibility, and feature-flagged opportunity notifications remain to be implemented.
- Compilation has not been verified in this environment because the .NET SDK is unavailable. The Angular zip contains only `src/`, not the workspace manifest (`package.json`, `angular.json`, and lockfile), so a full frontend build cannot be run from that archive alone.
