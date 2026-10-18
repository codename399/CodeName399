namespace CodeName399.Kite.API.Models;

public sealed class LtpDataResponse
{
    public bool Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public LtpData? Data { get; set; }
}

public sealed class LtpData
{
    public string Exchange { get; set; } = string.Empty;
    public string TradingSymbol { get; set; } = string.Empty;
    public string SymbolToken { get; set; } = string.Empty;
    public decimal Ltp { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal ReferenceLimitPrice { get; set; }
}

public sealed record ReferencePriceSnapshot(
    DateOnly Date,
    decimal ReferenceLimitPrice,
    decimal LowerBand,
    decimal UpperBand);
