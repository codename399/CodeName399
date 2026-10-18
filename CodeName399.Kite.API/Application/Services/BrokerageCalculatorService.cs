using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;

namespace CodeName399.Kite.API.Services.Brokerage;

public class BrokerageCalculatorService : IBrokerageCalculatorService
{
    private readonly IAngelOneService _angel;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BrokerageCalculatorService> _logger;

    public BrokerageCalculatorService(
        IAngelOneService angel,
        IMemoryCache cache,
        ILogger<BrokerageCalculatorService> logger
    )
    {
        _angel = angel;
        _cache = cache;
        _logger = logger;
    }

    public async Task<BrokerageCharge> CalculateAsync(
        string symbol,
        string symbolToken,
        decimal price,
        int quantity,
        BrokerageTransactionType transactionType,
        string exchange = "NSE",
        string productType = "INTRADAY"
    )
    {
        using var flow = TradingFlowActivity.Start(
            "trading.brokerage", symbol, symbolToken, direction: transactionType.ToString());

        _logger.LogDebug(
            "FLOW STAGE Brokerage.Start Symbol={Symbol}, Token={Token}, Exchange={Exchange}, Product={Product}, Transaction={Transaction}, Price={Price}, Quantity={Quantity}",
            symbol, symbolToken, exchange, productType, transactionType, price, quantity);

        string cacheKey = $"{exchange}_{productType}_{symbol}_{price:F2}_{quantity}_{transactionType}";

        if (_cache.TryGetValue(cacheKey, out BrokerageCharge? cached))
        {
            _logger.LogDebug("FLOW END Brokerage.CacheHit Symbol={Symbol}, Token={Token}, TotalCharges={TotalCharges}", symbol, symbolToken, cached!.TotalCharges);
            return cached!;
        }

        var request = new BrokerageEstimateRequest
        {
            Orders =
            [
                new BrokerageEstimateOrder
                {
                    Exchange = exchange,

                    ProductType = productType,

                    TransactionType =
                        transactionType == BrokerageTransactionType.Buy ? "BUY" : "SELL",

                    Quantity = quantity.ToString(),

                    Price = price.ToString(CultureInfo.InvariantCulture),

                    SymbolName = symbol,

                    Token = symbolToken,
                },
            ],
        };

        var response = await _angel.EstimateChargesAsync(request);

        if (!response.Status)
            throw new Exception(response.Message);

        var charge = new BrokerageCharge();

        foreach (var group in response.Data?.Summary?.Breakup ?? [])
        {
            if (group.Name.Equals("Angel One Brokerage", StringComparison.OrdinalIgnoreCase))
            {
                charge.Brokerage = group.Amount;
            }

            if (group.BreakupDetails == null)
                continue;

            foreach (var item in group.BreakupDetails)
            {
                switch (item.Name)
                {
                    case "Exchange Transaction Charges":
                        charge.ExchangeCharges = item.Amount;
                        break;

                    case "Stamp Duty":
                        charge.StampDuty = item.Amount;
                        break;

                    case "SEBI Fees":
                        charge.SEBICharges = item.Amount;
                        break;

                    case "Security Transaction Tax":
                        charge.STT = item.Amount;
                        break;

                    case "GST":
                        charge.GST = item.Amount;
                        break;
                }
            }
        }

        _cache.Set(cacheKey, charge, TimeSpan.FromSeconds(30));

        _logger.LogDebug("FLOW END Brokerage Symbol={Symbol}, Token={Token}, TotalCharges={TotalCharges}, Brokerage={Brokerage}, GST={GST}, STT={STT}, ExchangeCharges={ExchangeCharges}, StampDuty={StampDuty}",
            symbol, symbolToken, charge.TotalCharges, charge.Brokerage, charge.GST, charge.STT,
            charge.ExchangeCharges, charge.StampDuty);

        return charge;
    }
}
