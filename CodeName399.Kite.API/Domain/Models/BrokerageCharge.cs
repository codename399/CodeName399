namespace CodeName399.Kite.API.Services.Brokerage;

public class BrokerageCharge
{
    public decimal Brokerage { get; set; }

    public decimal STT { get; set; }

    public decimal ExchangeCharges { get; set; }

    public decimal SEBICharges { get; set; }

    public decimal GST { get; set; }

    public decimal StampDuty { get; set; }

    public decimal TotalCharges =>
        Brokerage + STT + ExchangeCharges + SEBICharges + GST + StampDuty;

    public decimal GrossProfit(decimal buyPrice, decimal sellPrice, int quantity)
    {
        return (sellPrice - buyPrice) * quantity;
    }

    public static BrokerageCharge Combine(BrokerageCharge buy, BrokerageCharge sell)
    {
        return new BrokerageCharge
        {
            Brokerage = buy.Brokerage + sell.Brokerage,
            STT = buy.STT + sell.STT,
            ExchangeCharges = buy.ExchangeCharges + sell.ExchangeCharges,
            SEBICharges = buy.SEBICharges + sell.SEBICharges,
            GST = buy.GST + sell.GST,
            StampDuty = buy.StampDuty + sell.StampDuty,
        };
    }
}
