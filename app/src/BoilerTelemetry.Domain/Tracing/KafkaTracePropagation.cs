using System.Diagnostics;
using System.Text;
using Confluent.Kafka;

namespace BoilerTelemetry.Domain.Tracing;

public static class KafkaTracePropagation
{
    public const string TraceParentHeader = "traceparent";

    public static void Inject(Activity? activity, Headers headers)
    {
        if (activity is null) return;
        var flags = activity.Recorded ? "01" : "00";
        var traceparent = $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{flags}";
        headers.Remove(TraceParentHeader);
        headers.Add(TraceParentHeader, Encoding.UTF8.GetBytes(traceparent));
    }

    public static ActivityContext Extract(Headers? headers)
    {
        if (headers is null) return default;
        if (!headers.TryGetLastBytes(TraceParentHeader, out var bytes)) return default;

        var traceparent = Encoding.UTF8.GetString(bytes);
        var parts = traceparent.Split('-');
        if (parts.Length != 4) return default;

        try
        {
            var traceId = ActivityTraceId.CreateFromString(parts[1]);
            var spanId = ActivitySpanId.CreateFromString(parts[2]);
            var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
            return new ActivityContext(traceId, spanId, flags, isRemote: true);
        }
        catch
        {
            return default;
        }
    }
}
