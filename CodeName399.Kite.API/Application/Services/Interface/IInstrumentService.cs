namespace CodeName399.Kite.API.Services.Interface
{
    public interface IInstrumentService
    {
        Task<List<Instrument>> LoadInstruments();
    }
}
