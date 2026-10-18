using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Services.Interface;

public interface IIndicatorService
{
    Task<IndicatorSnapshot?> GetAsync(
    MarketQuoteData stock,
    string jwtToken);
}