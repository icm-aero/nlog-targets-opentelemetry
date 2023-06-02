using System.Diagnostics;
using NLog.Common;
using NLog.LayoutRenderers;
using NLog.Layouts;
using NLog.OpenTelemetry;

namespace NLog.Targets.OpenTelemetry.NLog;

#pragma warning disable CS8618, CS1591
[LayoutRenderer(Id)]
public class OtelTraceContextLayout : Layout
{
    public const string Id = "OtelTraceContext";

    protected override string GetFormattedMessage(LogEventInfo logEvent)
    {
        return string.Empty;
    }
    public override void Precalculate(LogEventInfo logEvent)
    {
        try
        {
            var trace = new TraceContext
            {
                TraceId = Activity.Current?.TraceId.ToHexString()!,
                SpanId = Activity.Current?.SpanId.ToHexString()!
            };

            logEvent.Properties.Add(Id, trace);
        }
        catch (Exception e)
        {
            InternalLogger.Error(e.FlattenToActualException(), "Error processing Trace Context");
        }
    }

    public static TraceContext? RetrieveTraceContext(LogEventInfo logEvent)
    {
        if (logEvent.Properties.TryGetValue(Id, out var trace))
        {
            logEvent.Properties.Remove(Id);
            return trace as TraceContext;
        }

        return null;
    }
}

public class TraceContext
{
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
}