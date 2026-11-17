using CodeName399.Kite.API.Services;

namespace CodeName399.Kite.API.Application.BackgroundServices;

public sealed class RawMarketDataArchiveHostedService : IHostedService
{
    private readonly RawMarketDataArchiveService _archive;
    public RawMarketDataArchiveHostedService(RawMarketDataArchiveService archive) => _archive = archive;
    public Task StartAsync(CancellationToken cancellationToken) => _archive.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => _archive.StopAsync(cancellationToken);
}
