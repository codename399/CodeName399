namespace CodeName399.CandleBuilder.API.Services.Interface
{
    public interface IInstrumentService
    {
        Task<List<Instrument>> LoadInstruments();
    }
}
