using CodeName399.CandleBuilder.API.Services;

namespace CodeName399.CandleBuilder.API.Application.BackgroundServices;

public sealed class RawMarketDataArchiveHostedService : IHostedService
{
    private readonly RawMarketDataArchiveService _archive;
    public RawMarketDataArchiveHostedService(RawMarketDataArchiveService archive) => _archive = archive;
    public Task StartAsync(CancellationToken cancellationToken) => _archive.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => _archive.StopAsync(cancellationToken);
}
