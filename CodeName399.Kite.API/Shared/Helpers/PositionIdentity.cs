using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.Helpers;

public static class PositionIdentity
{
    public static string Create(string exchange, string symbolToken, string? symbol = null)
    {
        var token = Normalize(symbolToken);
        if (!string.IsNullOrWhiteSpace(token))
            return $"{Normalize(exchange)}:{token}";

        return $"{Normalize(exchange)}:SYMBOL:{Normalize(symbol ?? string.Empty)}";
    }

    public static string Create(Position position) =>
        Create(position.Exchange, position.SymbolToken, position.TradingSymbol);

    public static string Create(TradeOrder trade) =>
        Create(trade.Exchange, trade.SymbolToken, trade.Symbol);

    public static bool Matches(TradeOrder trade, Position position)
    {
        if (!string.IsNullOrWhiteSpace(trade.SymbolToken) &&
            !string.IsNullOrWhiteSpace(position.SymbolToken))
        {
            return string.Equals(trade.Exchange, position.Exchange, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(trade.SymbolToken, position.SymbolToken, StringComparison.OrdinalIgnoreCase);
        }

        if (trade.InstrumentType != Models.Enum.InstrumentType.Equity)
            return false;

        return string.Equals(trade.Symbol, position.TradingSymbol, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}
