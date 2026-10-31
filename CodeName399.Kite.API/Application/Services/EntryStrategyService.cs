using CodeName399.Kite.API.Domain.Models.Enum;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services
{
    public static class EntryStrategyService
    {
        public static bool ValidateBuyConditions(EntryStrategyEnum strategyEnum, MarketQuoteData quote, List<Candle> candles)
        {
            switch (strategyEnum)
            {
                case EntryStrategyEnum.EmaCrossOver:
                    return ValidateEmaCrossOver(quote);
                case EntryStrategyEnum.EmaCrossOverWithPullback:
                    // Implementation for EmaCrossOverWithPullback strategy
                    break;
                case EntryStrategyEnum.FVGWithSweep:
                    // Implementation for FVGWithSweep strategy
                    break;
                case EntryStrategyEnum.NoiseBand:
                    // Implementation for NoiseBand strategy
                    break;
            }

            return false; // Placeholder return value
        }

        private static bool ValidateEmaCrossOver(MarketQuoteData quote)
        {
            if (quote == null)
                return false;
            if (quote.Ema9 <= 0 || quote.Ema21 <= 0 || quote.PreviousCandleEma9 <= 0 || quote.PreviousCandleEma21 <= 0)
                return false;
            if (quote.ChoppinessIndex > 61.8m)
                return false;
            if (quote.Ltp <= quote.Ema9 || quote.Ltp <= quote.Ema21)
                return false;
            if (quote.Ema9 <= quote.Ema21)
                return false;
            if (quote.PreviousCandleEma9 >= quote.PreviousCandleEma21)
                return false;
            return true;
        }
    }
}
