using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services;

namespace CodeName399.Kite.API.BackgroundServices;

public sealed class ExitRecoveryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExitRecoveryBackgroundService> _logger;

    public ExitRecoveryBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExitRecoveryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var trades = scope.ServiceProvider.GetRequiredService<TradeManager>();
                var squareOff = scope.ServiceProvider.GetRequiredService<SquareOffService>();

                foreach (var trade in trades.GetOpen()
                    .Where(x => x.ExecutionMode == TradeExecutionMode.Live &&
                                x.ExitRequired &&
                                !x.ExitOrderPlaced &&
                                !string.IsNullOrWhiteSpace(x.SymbolToken))
                    .ToList())
                {
                    await squareOff.EmergencyCloseAsync(
                        trade,
                        string.IsNullOrWhiteSpace(trade.ExitReason)
                            ? "Recovered pending exit after application restart"
                            : trade.ExitReason);
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exit recovery iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
