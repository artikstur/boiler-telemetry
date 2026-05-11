using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace BoilerTelemetry.Domain.Tracing;

/// <summary>
/// Кладёт TraceId/SpanId текущего System.Diagnostics.Activity в каждую запись лога.
/// Так логи в OpenSearch связываются с трейсами в Jaeger по trace_id.
/// </summary>
public sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null) return;

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId",  activity.SpanId.ToHexString()));
        if (activity.ParentSpanId != default)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToHexString()));
    }
}
