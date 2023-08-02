using System.Text;
using Jedi.ServiceFabric.AspNetCore.Correlation;
using Jedi.ServiceFabric.Tracing;
using NLog;
using NLog.LayoutRenderers;
using OpenTelemetry;

namespace Jedi.ServiceFabric.Host.Logging
{
    [LayoutRenderer("CorellationId")]
    public class CorrelationIdLayoutRenderer : LayoutRenderer
    {
        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var correlationID = ServiceTracingContext.GetRequestCorrelationId();
            if (string.IsNullOrEmpty(correlationID))
            {
                correlationID = Baggage.GetBaggage(CorrelationIdMiddleware.BaggageCorrelationIdHeader);
            }
            builder.Append(correlationID);
        }
    }
}
