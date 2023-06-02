using System.Diagnostics;
using NLog.Common;
using NLog.LayoutRenderers;
using NLog.Layouts;
using NLog.OpenTelemetry;

namespace NLog.Targets.OpenTelemetry.NLog;

#pragma warning disable CS8618, CS1591
[LayoutRenderer(Id)]
public class OtelTagsLayout : Layout
{
    public const string Id = "OtelTags";

    protected override string GetFormattedMessage(LogEventInfo logEvent)
    {
        return string.Empty;
    }

    public override void Precalculate(LogEventInfo logEvent)
    {
        try
        {
            var tags = Activity.Current?.Tags
                ?.Where(pair => pair.Key?.StartsWith("baggage.") != true)
                ?.ToDictionary(pair => pair.Key, pair => pair.Value);
            logEvent.Properties.Add(Id, tags);
        }
        catch (Exception e)
        {
            InternalLogger.Error(e.FlattenToActualException(), "Error processing Trace Context Tags");
        }
    }

    public static Dictionary<string, string>? RetrieveTags(LogEventInfo logEvent)
    {
        if (logEvent.Properties.TryGetValue(Id, out var trace))
        {
            logEvent.Properties.Remove(Id);
            return trace as Dictionary<string, string>;
        }

        return null;
    }
}
