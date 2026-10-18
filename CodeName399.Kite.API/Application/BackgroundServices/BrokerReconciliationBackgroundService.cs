using CodeName399.Kite.API.Services;

namespace CodeName399.Kite.API.BackgroundServices
{
    public class BrokerReconciliationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<BrokerReconciliationBackgroundService> _logger;

        public BrokerReconciliationBackgroundService(
            IServiceProvider provider,
            ILogger<BrokerReconciliationBackgroundService> logger
        )
        {
            _provider = provider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _provider.CreateScope();

                    var sync =
                        scope.ServiceProvider.GetRequiredService<BrokerReconciliationService>();

                    await sync.ReconcileAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Broker reconciliation failed.");
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
