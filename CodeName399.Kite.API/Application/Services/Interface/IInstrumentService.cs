namespace CodeName399.Kite.API.Services.Interface
{
    public interface IInstrumentService
    {
        Task<List<Instrument>> LoadInstruments();
        Task<Instrument?> GetBySymbolAsync(string symbol);
        Task<Instrument?> GetByTokenAsync(string token);
        Task<Instrument?> GetByTokenAndExchangeAsync(string token, string exchange);
    }
}
