using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace CodeName399.Kite.API.Services;

/// <summary>Persists raw broker market feed as compact ZIP-compressed JSONL.</summary>
public sealed class RawMarketDataArchiveService : IHostedService, IDisposable
{
    private readonly TradingSettingsProvider _settings;

    private readonly ILogger<RawMarketDataArchiveService> _logger;
    private readonly MarketClock _marketClock;
    private readonly Channel<RawMarketTickRecord> _channel = Channel.CreateBounded<RawMarketTickRecord>(new BoundedChannelOptions(100_000)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cts = new();
    private Task? _writerTask;
    private long _dropped;

    public RawMarketDataArchiveService(
        ILogger<RawMarketDataArchiveService> logger,
        MarketClock marketClock,
        TradingSettingsProvider settings)
    {
        _logger = logger;
        _marketClock = marketClock;
        _settings = settings;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _writerTask = Task.Run(() => WriterLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public void Record(MarketTick tick)
    {
        if (tick == null || string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0) return;
        var raw = _settings.Current.RawMarketData ?? new RawMarketDataSettings();
        if (!raw.Enabled) return;

        // Raw live market-data recording is strictly session-bound.
        // Do not use MarketClock.IsMarketOpen here because IgnoreMarketHours is
        // intentionally a trading/test override and must not enable recording
        // outside the actual exchange session.
        if (raw.RecordOnlyDuringMarketHours && !_marketClock.IsRawMarketDataRecordingOpen()) return;
        var record = RawMarketTickRecord.From(tick);
        if (!_channel.Writer.TryWrite(record))
        {
            var dropped = Interlocked.Increment(ref _dropped);
            if (dropped == 1 || dropped % 10_000 == 0)
                _logger.LogWarning("Raw market-data archive queue is full. DroppedRecords={DroppedRecords}", dropped);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        if (_writerTask != null)
        {
            try { await _writerTask.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); }
            catch (TimeoutException) { _logger.LogWarning("Raw market-data archive did not drain within shutdown timeout."); _cts.Cancel(); }
        }
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        FileStream? file = null;
        ZipArchive? archive = null;
        Stream? entry = null;
        DateTime currentDate = default;
        var part = 0;

        async Task CloseArchiveAsync()
        {
            if (entry != null) await entry.DisposeAsync();
            entry = null;
            archive?.Dispose();
            archive = null;
            if (file != null) await file.DisposeAsync();
            file = null;
        }

        async Task OpenArchiveAsync(DateTime date)
        {
            await CloseArchiveAsync();
            var raw = _settings.Current.RawMarketData ?? new RawMarketDataSettings();
            var rootPath = ResolveStoragePath(raw.StoragePath);
            Directory.CreateDirectory(rootPath);
            var directory = Path.Combine(rootPath, date.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(directory);
            part++;
            var path = Path.Combine(directory, $"market-ticks-{DateTime.UtcNow:HHmmssfff}-part{part:000}.zip");
            file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 256 * 1024, useAsync: true);
            archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true);
            entry = archive.CreateEntry("market-ticks.jsonl", CompressionLevel.Fastest).Open();
            currentDate = date;
        }

        try
        {
            await foreach (var record in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var date = record.ExchangeTimeUtc == default ? DateTime.UtcNow.Date : record.ExchangeTimeUtc.Date;
                    if (archive == null || date != currentDate)
                        await OpenArchiveAsync(date);

                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record) + "\n");
                    var raw = _settings.Current.RawMarketData ?? new RawMarketDataSettings();
                    var maxZipBytes = raw.MaximumZipSizeMb * 1024L * 1024L;
                    var rollThresholdBytes = maxZipBytes * raw.RollOverAtPercent / 100L;
                    if (file!.Position >= rollThresholdBytes && file.Position > 0)
                        await OpenArchiveAsync(date);

                    await entry!.WriteAsync(bytes, cancellationToken);

                    if (file.Position > maxZipBytes)
                    {
                        _logger.LogWarning("Raw market-data ZIP crossed configured maximum after a single record. MaximumZipSizeMb={MaximumZipSizeMb}, RecordSize={Bytes}, File={File}", raw.MaximumZipSizeMb, bytes.Length, file.Name);
                        await OpenArchiveAsync(date);
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to persist raw market tick for {SymbolToken}", record.SymbolToken); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            await CloseArchiveAsync();
            if (_dropped > 0) _logger.LogWarning("Raw market-data archive stopped with dropped records. DroppedRecords={DroppedRecords}", _dropped);
        }
    }

    

    private static string ResolveStoragePath(string configuredPath)
    {
        // Backward-compatible deployment override. Normal configuration comes from TradingConfiguration.
        var environmentOverride = Environment.GetEnvironmentVariable("CODENAME399_RAW_MARKET_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(environmentOverride)) return environmentOverride;
        if (Path.IsPathRooted(configuredPath)) return configuredPath;
        return Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    public void Dispose() => _cts.Dispose();
}

public sealed record RawMarketTickRecord(
    string TradingSymbol,
    string SymbolToken,
    int ExchangeType,
    int MarketDataMode,
    long SequenceNumber,
    DateTime ExchangeTimeUtc,
    DateTime ReceivedTimeUtc,
    decimal Ltp,
    decimal Bid,
    decimal Ask,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal PreviousClose,
    long LastTradedQuantity,
    decimal AveragePrice,
    long DayVolume,
    long Volume,
    double TotalBuyQuantity,
    double TotalSellQuantity,
    IReadOnlyList<MarketDepthLevel> BidDepth,
    IReadOnlyList<MarketDepthLevel> AskDepth)
{
    public static RawMarketTickRecord From(MarketTick t) => new(
        t.TradingSymbol, t.SymbolToken, t.ExchangeType, t.MarketDataMode, t.SequenceNumber,
        t.ExchangeTime, t.LastUpdated, t.Ltp, t.Bid, t.Ask, t.Open, t.High, t.Low, t.Close,
        t.PreviousClose, t.LastTradedQuantity, t.AveragePrice, t.DayVolume, t.Volume,
        t.TotalBuyQuantity, t.TotalSellQuantity, t.BidDepth.ToArray(), t.AskDepth.ToArray());
}
