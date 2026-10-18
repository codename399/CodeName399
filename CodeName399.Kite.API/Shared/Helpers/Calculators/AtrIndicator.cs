using CodeName399.Kite.API.Engine;
using CodeName399.Kite.API.Helpers.Calculators;

namespace CodeName399.Kite.API.Indicators.Calculators;

public sealed class AtrIndicator
{
    public decimal Calculate(IndicatorContext context)
        => IndicatorHelper.CalculateATR(context.Candles5m);
}
