namespace CodeName399.Kite.API.Services.Brokerage;

public interface IBrokerageCalculatorService
{
    Task<BrokerageCharge> CalculateAsync(
        string symbol,
        string symbolToken,
        decimal price,
        int quantity,
        BrokerageTransactionType transactionType,
        string exchange = "NSE",
        string productType = "INTRADAY");
}