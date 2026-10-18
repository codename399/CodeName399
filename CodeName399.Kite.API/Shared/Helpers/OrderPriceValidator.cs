namespace CodeName399.Kite.API.Helpers;

public static class OrderPriceValidator
{
    private const decimal TickSize = 0.05m;

    public static decimal RoundDown(decimal price)
    {
        return Math.Floor(price / TickSize) * TickSize;
    }

    public static decimal RoundUp(decimal price)
    {
        return Math.Ceiling(price / TickSize) * TickSize;
    }

    public static decimal ValidateStopLoss(decimal stopLoss, decimal lowerCircuit)
    {
        stopLoss = RoundDown(stopLoss);

        // Circuit limit not available
        if (lowerCircuit <= 0)
            return stopLoss;

        if (stopLoss <= lowerCircuit)
            stopLoss = RoundUp(lowerCircuit + TickSize);

        return stopLoss;
    }

}