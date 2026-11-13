using CodeName399.CandleBuilder.API.Models;

public interface IAngelOneService
{
    Task<AngelOneLoginData> GetLoginDataAsync();
}
