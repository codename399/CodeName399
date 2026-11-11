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

        string cacheKey = $"{exchange}_{productType}_{symbol}_{price.ToString("G29", CultureInfo.InvariantCulture)}_{quantity}_{transactionType}";

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

        var charge = new BrokerageCharge
        {
            ReportedTotalCharges = response.Data?.Summary?.TotalCharges > 0m
                ? response.Data.Summary.TotalCharges
                : null
        };

        foreach (var group in response.Data?.Summary?.Breakup ?? [])
        {
            if (group == null) continue;

            if (string.Equals(group.Name, "Angel One Brokerage", StringComparison.OrdinalIgnoreCase))
                charge.Brokerage = group.Amount;

            foreach (var item in group.BreakupDetails ?? [])
            {
                if (item == null) continue;
                switch ((item.Name ?? string.Empty).Trim().ToUpperInvariant())
                {
                    case "EXCHANGE TRANSACTION CHARGES":
                        charge.ExchangeCharges += item.Amount;
                        break;
                    case "STAMP DUTY":
                        charge.StampDuty += item.Amount;
                        break;
                    case "SEBI FEES":
                    case "SEBI CHARGES":
                        charge.SEBICharges += item.Amount;
                        break;
                    case "SECURITY TRANSACTION TAX":
                    case "STT":
                        charge.STT += item.Amount;
                        break;
                    case "GST":
                        charge.GST += item.Amount;
                        break;
                }
            }
        }

        if (charge.TotalCharges < 0m || charge.TotalCharges > Math.Max(100m, price * quantity * 0.10m))
        {
            _logger.LogError(
                "BROKERAGE ESTIMATE OUT OF RANGE Symbol={Symbol} Token={Token} Exchange={Exchange} Product={Product} Transaction={Transaction} Price={Price} Quantity={Quantity} TradeValue={TradeValue} ReportedTotal={ReportedTotal} ComponentTotal={ComponentTotal}. Rejecting estimate.",
                symbol, symbolToken, exchange, productType, transactionType, price, quantity,
                price * quantity, charge.ReportedTotalCharges, charge.Brokerage + charge.GST + charge.STT + charge.ExchangeCharges + charge.SEBICharges + charge.StampDuty);
            throw new InvalidOperationException($"Brokerage estimate is outside the allowed sanity range for {symbol}.");
        }

        _logger.LogInformation(
            "BROKERAGE ESTIMATE Symbol={Symbol} Token={Token} Exchange={Exchange} Product={Product} Transaction={Transaction} Price={Price:F4} Quantity={Quantity} TradeValue={TradeValue:F2} ReportedTotal={ReportedTotal:F4} CalculatedTotal={CalculatedTotal:F4} Brokerage={Brokerage:F4} GST={GST:F4} STT={STT:F4} ExchangeCharges={ExchangeCharges:F4} SEBI={SEBI:F4} StampDuty={StampDuty:F4}",
            symbol, symbolToken, exchange, productType, transactionType, price, quantity,
            price * quantity, charge.ReportedTotalCharges, charge.TotalCharges, charge.Brokerage,
            charge.GST, charge.STT, charge.ExchangeCharges, charge.SEBICharges, charge.StampDuty);

        _cache.Set(cacheKey, charge, TimeSpan.FromSeconds(30));

        _logger.LogDebug("FLOW END Brokerage Symbol={Symbol}, Token={Token}, TotalCharges={TotalCharges}, Brokerage={Brokerage}, GST={GST}, STT={STT}, ExchangeCharges={ExchangeCharges}, StampDuty={StampDuty}",
            symbol, symbolToken, charge.TotalCharges, charge.Brokerage, charge.GST, charge.STT,
            charge.ExchangeCharges, charge.StampDuty);

        return charge;
    }
}
