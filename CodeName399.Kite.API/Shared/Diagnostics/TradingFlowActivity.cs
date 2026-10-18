using System.Diagnostics;

namespace CodeName399.Kite.API.Diagnostics;

public static class TradingFlowActivity
{
    public static Activity Start(
        string operationName,
        string? symbol = null,
        string? symbolToken = null,
        string? instrumentType = null,
        string? direction = null)
    {
        var activity = new Activity(operationName);

        if (!string.IsNullOrWhiteSpace(symbol))
            activity.SetTag("trading.symbol", symbol);

        if (!string.IsNullOrWhiteSpace(symbolToken))
            activity.SetTag("trading.symbolToken", symbolToken);

        if (!string.IsNullOrWhiteSpace(instrumentType))
            activity.SetTag("trading.instrumentType", instrumentType);

        if (!string.IsNullOrWhiteSpace(direction))
            activity.SetTag("trading.direction", direction);

        activity.Start();
        return activity;
    }

    public static string TraceId =>
        Activity.Current?.TraceId.ToString() ?? "no-trace";

    public static string SpanId =>
        Activity.Current?.SpanId.ToString() ?? "no-span";
}
