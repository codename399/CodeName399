API changes:

Flow: 
1. load equity instruments
2. fetch quotes
3. filter only top 10 gainers which are highly active and highly traded with positive change percent
4. apply pagination to list of gainers with default page size as 10(configurable)
5. subscribe this filtered stocks
6. add minimum buy quantity to gainers which allows over coming charges with minimum price change of default 0.25%(configurable)
7. minimum buy quantity should be calculated on every tick
8. add buy endpoint to buy stocks
9. add sell endpoint to sell stocks
10. add endpoint to load candles for that day
11. add subscribe endpoint to subscribe
12. add sendemail endpoint
13. add search endpoint
14. keep paper trading, auto trading and paper as live trading options
15. update ocelot to have all the required endpoints and remove unwanted endpoints
16. add paper as live trading
17. for live trading use actual balance from platform, for paper trading use virtual balance, for paper as live virtual balance is deducted in memory same as live trading and updated to db on market close

UI:
1. show gainers returned from signalr
2. use signalr to show up to date tick data
3. use server side pagination
4. grid columns: stock,ltp,minimum quantity,charges, minimum investment amount with charges,profit loss if owned,buy button if not owned, sell button if already owned
5. use icons for buttons
6. add search button to perform server side stock search in list of gainers
7. add indicator column to show if last candle close was a pullback
8. sort gainers as first owned,second pullback,then rest
9. send notification when owned stock achieves positive net profit and sell opportunity is there or any stocks candle closes with pullback and buy opportunity is there
10. give option to subscribe or unsubscribe any stock
11. manually subcribed stock should be sorted after owned stocks
12. add indicator to show if enough fund is available to buy specific stock
13. keep opportunity notification behind different feature flags
14. dont show buy sell buttons during non market hours
15. make stock names clickable and do not allow removing it from visible columns list
16. when stock name is clicked navigate to trading component
17. on load of trading component load all the current days candles for the selected stock
18. display all the stock related details on trade component, also show details which is available for a owned stock
19. show a live graph which displays all the loaded historical candles as a blue dot, connect candles using green line for uptrend and red line for downtrend
20. also show ema9 and ema21 indicators on the graph with some other colors
21. calcualte indicators on everytick on trade component
22. allow enable and disable of indicators
23. first show all the candles from api and keep on using live ticks to generate further candles
24. highlight ema crossovers with a colored cross
25. show all the prices on the graph
26. allow to select any point on the chart and read all the details at that point like prices, indicators ,timestamps and all other relevant details
27. use indian timezone everywhere
28. add return to dashboard button (use icon)
29. also show all the buy sell related information on trade component which we are showing on the dashboard
30. there should be 2 buy options both in trade component and also on dashboard, i. minimum buy which buys minimum quantity to cover up charges with minimum change of 0.25%(configurable) and ii. maximum buy which buys maximum quantity which can be bought using available balance (using maximum trade percent for each trade)

Features to remove:
1. remove market hours check from everywhere except for buy and sell
2. remove all prediction related configuration and codes
3. remove auto buy and sell it should only be done using endpoint
4. remove historical candle loading,caching and candles should only be returned to the newly added endpoint
5. remove all the non needed configurations

Scan the entire zip properly
Make above changes and remove unwanted implementations and configurations
Add an md file containing all these requirements
Dont make random changes
Make sure compilation succeeds
