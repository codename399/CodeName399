using CodeName399.Kite.API.Models.WebSocketOrder;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public class OrderStatusCache
{
    private readonly ConcurrentDictionary<string, OrderStatusMessage> _orders = new();

    private readonly ConcurrentDictionary<string, List<OrderStatusMessage>> _history = new();

    public void Set(OrderStatusMessage order)
    {
        _orders[order.OrderId] = order;

        _history.AddOrUpdate(
            order.OrderId,
            _ => new List<OrderStatusMessage>
            {
            order
            },
            (_, list) =>
            {
                lock (list)
                {
                    list.Add(order);
                }

                return list;
            });
    }

    public OrderStatusMessage? Get(string orderId)
    {
        _orders.TryGetValue(orderId, out var order);

        return order;
    }

    public IReadOnlyCollection<OrderStatusMessage> GetAll()
    {
        return _orders.Values.ToList();
    }

    public bool Exists(string orderId)
    {
        return _orders.ContainsKey(orderId);
    }

    public void Remove(string orderId)
    {
        _orders.TryRemove(orderId, out _);
    }

    public void Clear()
    {
        _orders.Clear();
    }

    public IReadOnlyList<OrderStatusMessage> History(string orderId)
    {
        if (_history.TryGetValue(orderId, out var list))
            return list;

        return Array.Empty<OrderStatusMessage>();
    }
}