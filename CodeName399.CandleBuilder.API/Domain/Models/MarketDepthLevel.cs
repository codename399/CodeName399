namespace CodeName399.CandleBuilder.API.Models;

public sealed class MarketDepthLevel
{
    public decimal Price { get; set; }
    public long Quantity { get; set; }
    public int OrderCount { get; set; }
}
