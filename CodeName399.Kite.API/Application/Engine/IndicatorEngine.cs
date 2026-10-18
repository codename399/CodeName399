using CodeName399.Kite.API.Engine;
using CodeName399.Kite.API.Indicators.Calculators;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Indicators.Engine;

/// <summary>
/// Calculates only the technical data required by the Core strategy:
/// ATR for protective risk management. Recovery, price change and breakout
/// are derived directly by indicator calculations from completed 1-minute candles.
/// </summary>
public sealed class IndicatorEngine
{
    private readonly AtrIndicator _atr;

    public IndicatorEngine(AtrIndicator atr) => _atr = atr;

    public IndicatorSnapshot Calculate(IndicatorContext context)
    {
        var price = context.CurrentPrice;
        var changePercent = context.PreviousClose > 0m
            ? Math.Round((price - context.PreviousClose) / context.PreviousClose * 100m, 2)
            : 0m;

        return new IndicatorSnapshot
        {
            Candles1m = context.Candles1m.ToList(),
            Candles5m = context.Candles5m.ToList(),
            CurrentPrice = price,
            PreviousClose = context.PreviousClose,
            ATR = _atr.Calculate(context),
            ChangePercent = changePercent
        };
    }
}
