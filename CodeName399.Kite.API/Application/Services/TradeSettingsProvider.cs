using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Repositories.Interface;
using System.Text.Json;
using System.Text.Json.Serialization;

public class TradingSettingsProvider
{
    private readonly ITradingConfigurationRepository _repository;
    private volatile TradingConfiguration _current = new();
    private volatile string _currentJson = "{}";
    private long _version;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TradingSettingsProvider(ITradingConfigurationRepository repository)
    {
        _repository = repository;
        _current = _repository.GetAsync().GetAwaiter().GetResult();
        RefreshSnapshot();
    }

    public TradingConfiguration Current => _current;
    public string CurrentJson => _currentJson;
    public long Version => Interlocked.Read(ref _version);
    public event Action<TradingConfiguration>? ConfigurationChanged;

    public async Task ReloadAsync()
    {
        var configuration = await _repository.GetAsync();
        UpdateSnapshot(configuration);
    }

    public async Task UpdateAsync(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _repository.UpdateAsync(configuration);
        UpdateSnapshot(configuration);
    }

    public void Update(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        UpdateSnapshot(configuration);
    }

    private void UpdateSnapshot(TradingConfiguration configuration)
    {
        _current = configuration;
        RefreshSnapshot();
    }

    private void RefreshSnapshot()
    {
        _currentJson = Serialize(_current);
        Interlocked.Increment(ref _version);
        ConfigurationChanged?.Invoke(_current);
    }
    public static string Serialize(TradingConfiguration configuration)
        => JsonSerializer.Serialize(configuration, JsonOptions);

    public static TradingConfiguration Clone(TradingConfiguration configuration)
        => JsonSerializer.Deserialize<TradingConfiguration>(Serialize(configuration), JsonOptions)
           ?? throw new InvalidOperationException("Unable to clone TradingConfiguration.");
}
