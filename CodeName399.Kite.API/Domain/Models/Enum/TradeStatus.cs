namespace CodeName399.Kite.API.Models;

public enum TradeStatus
{
    // Keep persisted numeric values stable for existing MongoDB trades.
    PendingBrokerConfirmation = 0,
    Open = 1,
    Closed = 2,
    Cancelled = 3,
    Rejected = 4,
    PartiallyFilled = 5
}
