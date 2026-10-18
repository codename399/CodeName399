using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.Services;

/// <summary>
/// Central SmartAPI/NSE CAS guard.  CAS applies to eligible NSE/BSE cash scrips.
/// F&O has an extended session, but intraday F&O auto-square-off remains at the
/// broker-documented 15:20 cut-off.
/// </summary>
public sealed class CasTradingRulesService
{
    private readonly TradingSettingsProvider _settings;
    private readonly ILogger<CasTradingRulesService> _logger;

    public CasTradingRulesService(TradingSettingsProvider settings, ILogger<CasTradingRulesService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public CasValidationResult ValidatePlace(PlaceOrderRequest request, DateTime now)
    {
        if (!IsCashCasOrder(request))
            return CasValidationResult.AllowedResult;

        var t = now.TimeOfDay;
        if (IsBetween(t, _settings.Current.CasTransitionStart, _settings.Current.CasOrderEntryStart))
            return Reject("CAS transition is active; SmartAPI order entry is rejected from 15:15 to 15:20.");

        if (IsBetween(t, _settings.Current.CasOrderEntryStart, _settings.Current.CasMarketOnlyEnd))
        {
            if (IsStopLossOrIceberg(request))
                return Reject("STOPLOSS and ICEBERG orders are not allowed for CAS-eligible scrips from 15:15 to 15:40.");
            return CasValidationResult.AllowedResult;
        }

        if (IsBetween(t, _settings.Current.CasMarketOnlyEnd, _settings.Current.CasLimitOnlyEnd))
        {
            if (t >= _settings.Current.CasRandomCloseSafetyCutoff)
                return Reject("CAS random close can occur from 15:28; new LIMIT orders are conservatively blocked after the configured safety cutoff.");
            if (IsStopLossOrIceberg(request))
                return Reject("STOPLOSS and ICEBERG orders are not allowed for CAS-eligible scrips from 15:15 to 15:40.");
            if (!request.OrderType.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
                return Reject("Only LIMIT orders are allowed for CAS-eligible scrips after 15:25 during CAS.");
            return CasValidationResult.AllowedResult;
        }

        if (IsBetween(t, _settings.Current.CasLimitOnlyEnd, _settings.Current.CasEnd))
            return Reject("SmartAPI order entry is blocked during CAS matching/buffer for CAS-eligible scrips.");

        return CasValidationResult.AllowedResult;
    }

    public CasValidationResult ValidateModify(ModifyOrderRequest request, DateTime now)
    {
        if (!request.IsCasEligible || !IsCashExchange(request.Exchange))
            return CasValidationResult.AllowedResult;

        var t = now.TimeOfDay;
        if (IsBetween(t, _settings.Current.CasTransitionStart, _settings.Current.CasOrderEntryStart))
            return Reject("Order modification is blocked during the 15:15-15:20 CAS transition.");

        if (IsBetween(t, _settings.Current.CasOrderEntryStart, _settings.Current.CasMarketOnlyEnd))
        {
            if (IsStopLossOrIceberg(request))
                return Reject("STOPLOSS and ICEBERG modifications are not allowed during CAS.");
            return CasValidationResult.AllowedResult;
        }

        if (IsBetween(t, _settings.Current.CasMarketOnlyEnd, _settings.Current.CasLimitOnlyEnd))
        {
            if (t >= _settings.Current.CasRandomCloseSafetyCutoff)
                return Reject("CAS random close can occur from 15:28; modifications are conservatively blocked after the configured safety cutoff.");
            if (request.OrderType.Equals("MARKET", StringComparison.OrdinalIgnoreCase))
                return Reject("Market orders cannot be modified after 15:25 during CAS.");
            if (IsStopLossOrIceberg(request))
                return Reject("STOPLOSS and ICEBERG modifications are not allowed during CAS.");
            if (!request.OrderType.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
                return Reject("Only LIMIT orders can be modified after 15:25 during CAS.");
            return CasValidationResult.AllowedResult;
        }

        if (t >= _settings.Current.CasLimitOnlyEnd && t < _settings.Current.CasPostCloseEnd)
            return Reject("Order modification is blocked during CAS matching/buffer/post-close transition.");

        return CasValidationResult.AllowedResult;
    }

    public CasValidationResult ValidateCancel(string exchange, string orderType, bool casEligible, DateTime now)
    {
        if (!casEligible || !IsCashExchange(exchange))
            return CasValidationResult.AllowedResult;

        var t = now.TimeOfDay;
        if (IsBetween(t, _settings.Current.CasMarketOnlyEnd, _settings.Current.CasLimitOnlyEnd))
        {
            if (t >= _settings.Current.CasRandomCloseSafetyCutoff)
                return Reject("CAS random close can occur from 15:28; cancellation is conservatively blocked after the configured safety cutoff.");
            if (orderType.Equals("MARKET", StringComparison.OrdinalIgnoreCase))
                return Reject("Market orders cannot be cancelled after 15:25 during CAS.");
        }

        if (t >= _settings.Current.CasLimitOnlyEnd && t < _settings.Current.CasPostCloseEnd)
            return Reject("Order cancellation is blocked during CAS matching/buffer/post-close transition.");

        return CasValidationResult.AllowedResult;
    }

    private bool IsCashCasOrder(PlaceOrderRequest request)
        => request.IsCasEligible && IsCashExchange(request.Exchange);

    private static bool IsCashExchange(string exchange)
        => exchange.Equals("NSE", StringComparison.OrdinalIgnoreCase)
           || exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase);

    private static bool IsStopLossOrIceberg(PlaceOrderRequest request)
        => request.OrderType.Equals("STOPLOSS", StringComparison.OrdinalIgnoreCase)
           || request.Variety.Contains("ICEBERG", StringComparison.OrdinalIgnoreCase);

    private static bool IsStopLossOrIceberg(ModifyOrderRequest request)
        => request.OrderType.Equals("STOPLOSS", StringComparison.OrdinalIgnoreCase)
           || request.Variety.Contains("ICEBERG", StringComparison.OrdinalIgnoreCase);

    private static bool IsBetween(TimeSpan value, TimeSpan startInclusive, TimeSpan endExclusive)
        => value >= startInclusive && value < endExclusive;

    private CasValidationResult Reject(string reason)
    {
        _logger.LogWarning("CAS order rejected: {Reason}", reason);
        return new CasValidationResult(false, reason);
    }
}

public readonly record struct CasValidationResult(bool Allowed, string Reason)
{
    public static CasValidationResult AllowedResult => new(true, string.Empty);
}
