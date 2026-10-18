using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Models.WebSocketOrder;

public class OrderStatusMessage
{
    public string OrderId { get; set; } = "";

    public string Symbol { get; set; } = "";

    public string SymbolToken { get; set; } = "";

    public string Status { get; set; } = "";

    public string TransactionType { get; set; } = "";

    public string ProductType { get; set; } = "";

    public string Variety { get; set; } = "";

    public string OrderType { get; set; } = "";

    public decimal AveragePrice { get; set; }

    public int FilledQuantity { get; set; }

    public int Quantity { get; set; }

    public string Exchange { get; set; } = "";

    public string RejectionReason { get; set; } = "";

    public string OrderTag { get; set; } = "";

    public DateTime UpdatedTime { get; set; }

    public OrderExecutionState ExecutionState
    {
        get
        {
            return Status.ToUpperInvariant() switch
            {
                "PARTIALLY_FILLED" or "PARTIALLY FILLED" => OrderExecutionState.PartiallyFilled,
                "OPEN" => OrderExecutionState.Open,
                "TRIGGER PENDING" => OrderExecutionState.TriggerPending,
                "COMPLETE" => OrderExecutionState.Complete,
                "REJECTED" => OrderExecutionState.Rejected,
                "CANCELLED" => OrderExecutionState.Cancelled,
                "MODIFIED" => OrderExecutionState.Modified,
                _ => OrderExecutionState.Submitted
            };
        }
    }
}