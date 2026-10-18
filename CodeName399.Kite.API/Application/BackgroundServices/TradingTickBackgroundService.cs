using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.BackgroundServices;

public sealed class TradingTickBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAngelWebSocketClient _webSocket;
    private readonly ILogger<TradingTickBackgroundService> _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _tickGates = new(StringComparer.OrdinalIgnoreCase);

    public TradingTickBackgroundService(
        IServiceProvider serviceProvider,
        IAngelWebSocketClient webSocket,
        ILogger<TradingTickBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _webSocket = webSocket;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _webSocket.TickReceived += OnTick;
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _webSocket.TickReceived -= OnTick;
            foreach (var gate in _tickGates.Values)
                gate.Dispose();
            _tickGates.Clear();
        }
    }

    private async void OnTick(object? sender, MarketTick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.SymbolToken))
            return;

        var gate = _tickGates.GetOrAdd(tick.SymbolToken, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        using var flow = TradingFlowActivity.Start("trading.tick-dispatch", symbolToken: tick.SymbolToken);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var tradeManager = scope.ServiceProvider.GetRequiredService<TradeManager>();

            var openTrades = tradeManager.GetOpenTrades().ToList();
            var watchList = scope.ServiceProvider.GetRequiredService<WatchListService>();

            foreach (var openTrade in openTrades
                .Where(x => string.Equals(x.SymbolToken, tick.SymbolToken, StringComparison.OrdinalIgnoreCase)))
            {
                var price = tick.Ltp > 0 ? tick.Ltp : openTrade.CurrentPrice;
                if (price <= 0) continue;
                openTrade.CurrentPrice = price;
                openTrade.UpdateResearchPath(tick);
                openTrade.HighestPrice = openTrade.HighestPrice > 0 ? Math.Max(openTrade.HighestPrice, price) : price;
                openTrade.LowestPrice = openTrade.LowestPrice > 0 ? Math.Min(openTrade.LowestPrice, price) : price;
                await tradeManager.UpdateAsync(openTrade);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FLOW ERROR Tick.Dispatch Token={Token}", tick.SymbolToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
