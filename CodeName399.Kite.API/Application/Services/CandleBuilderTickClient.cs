using CodeName399.Kite.API.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>Receives the live tick stream published by CandleBuilder.API.</summary>
public sealed class CandleBuilderTickClient : BackgroundService
{
    private readonly ILogger<CandleBuilderTickClient> _logger;
    private readonly string _url;
    private HubConnection? _connection;
    public event EventHandler<MarketTick>? TickReceived;
    public CandleBuilderTickClient(ILogger<CandleBuilderTickClient> logger)
    {
        _logger=logger;
        _url=Environment.GetEnvironmentVariable("CODENAME399_CANDLEBUILDER_HUB_URL") ?? "http://127.0.0.1:5005/ticksHub";
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _connection=new HubConnectionBuilder().WithUrl(_url).WithAutomaticReconnect().Build();
        _connection.On<MarketTick>("TickReceived", tick => TickReceived?.Invoke(this,tick));
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if(_connection.State==HubConnectionState.Disconnected) await _connection.StartAsync(stoppingToken);
                await Task.Delay(1000,stoppingToken);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception ex) { _logger.LogWarning(ex,"Unable to connect to CandleBuilder SignalR hub {HubUrl}; retrying.",_url); await Task.Delay(3000,stoppingToken); }
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if(_connection!=null) { await _connection.DisposeAsync(); _connection=null; }
        await base.StopAsync(cancellationToken);
    }
}
