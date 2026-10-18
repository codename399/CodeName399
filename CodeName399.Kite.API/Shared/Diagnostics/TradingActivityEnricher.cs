using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;

namespace CodeName399.Kite.API.Diagnostics;

/// <summary>
/// Adds the current System.Diagnostics Activity identifiers to every Serilog
/// event. This makes a complete trading flow searchable across background
/// services, scanner/evaluation, validation, order placement and broker updates.
/// </summary>
public sealed class TradingActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty(
                "FlowId",
                activity?.TraceId.ToString() ?? "no-trace"));

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty(
                "SpanId",
                activity?.SpanId.ToString() ?? "no-span"));

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty(
                "FlowOperation",
                activity?.OperationName ?? "none"));
    }
}
