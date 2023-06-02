using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.OpenTelemetry;
using OpenTelemetry;

namespace NLog.Targets.OpenTelemetry.NLog;

#pragma warning disable CS8618, CS1591

[ThreadAgnostic]
[ThreadSafe]
public class OtelBaggageLayout : Layout
{
    public const string Id = "OtelBaggage";

    protected override string GetFormattedMessage(LogEventInfo logEvent)
    {
        return string.Empty;
    }

    public override void Precalculate(LogEventInfo logEvent)
    {
        try
        {
            var baggage = Baggage.Current.GetBaggage();
            logEvent.Properties.Add(Id, baggage);
        }
        catch (Exception e)
        {
            InternalLogger.Error(e.FlattenToActualException(), "Error processing Trace Context");
        }


        base.Precalculate(logEvent);
    }

    public static Dictionary<string, string>? RetrieveBaggage(LogEventInfo logEvent)
    {
        if (logEvent.Properties.TryGetValue(Id, out var trace))
        {
            logEvent.Properties.Remove(Id);
            return trace as Dictionary<string, string>;
        }

        return null;
    }
}
