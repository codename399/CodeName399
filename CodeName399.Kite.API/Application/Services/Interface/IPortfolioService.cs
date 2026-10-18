namespace CodeName399.Kite.API.Services.Interface
{
    public interface IPortfolioService
    {
        Task<HashSet<string>> GetOwnedSymbolsAsync();
    }
}
