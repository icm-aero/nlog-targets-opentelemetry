#pragma warning disable CS8600
#pragma warning disable CS8603

using Jedi.ServiceFabric.Tracing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenTelemetry;

namespace Jedi.ServiceFabric.AspNetCore.Correlation
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly CorrelationIdOptions _options;
        public static readonly string BaggageCorrelationIdHeader = CorrelationIdOptions.DefaultHeader.ToLower();


        /// <summary>
        /// Creates a new instance of the CorrelationIdMiddleware.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="options">The configuration options.</param>
        public CorrelationIdMiddleware(RequestDelegate next, IOptions<CorrelationIdOptions> options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if(next == null) throw new ArgumentNullException(nameof(next));
            _next = next;

            _options = options.Value;
        }

        /// <summary>
        /// Processes a request to synchronise TraceIdentifier and Correlation ID headers
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task Invoke(HttpContext context)
        {
            StringValues correlationId;

            if (context.Request.Headers.TryGetValue(_options.Header, out correlationId) &&
                !string.IsNullOrEmpty(correlationId))
            {
                context.TraceIdentifier = correlationId;
                ServiceTracingContext.SetRequestCorrelationId(correlationId);
            }

            if (_options.IncludeInResponse)
            {
                // apply the correlation ID to the response header for client side tracking
                context.Response.OnStarting(() =>
                {
                    var correlationIdString = ServiceTracingContext.GetRequestCorrelationId();
                    if (!string.IsNullOrEmpty(correlationIdString))
                    {
                        context.Response.Headers.Add(_options.Header, new[] {correlationIdString});
                    }
                    return Task.CompletedTask;
                });
            }

            return _next(context);
        }

        public static string GetCorrelationId(HttpRequest context, CorrelationIdOptions options)
        {
            if (context != null && context.Headers.TryGetValue(options.Header, out var correlationId) &&
                !string.IsNullOrEmpty(correlationId))
            {
                SetCorrelationId(correlationId);
                return correlationId;
            }

            var baggageCorrelationId = Baggage.GetBaggage(BaggageCorrelationIdHeader);
            if (!string.IsNullOrEmpty(baggageCorrelationId))
            {
                ServiceTracingContext.SetRequestCorrelationId(baggageCorrelationId);
                return baggageCorrelationId;
            }

            var currentCorrelationId = ServiceTracingContext.GetRequestCorrelationId();
            if (!string.IsNullOrEmpty(currentCorrelationId))
            {
                Baggage.SetBaggage(BaggageCorrelationIdHeader, currentCorrelationId);
                return currentCorrelationId;
            }

            return null;
        }

        public static void SetCorrelationId(string correlationId)
        {
            ServiceTracingContext.SetRequestCorrelationId(correlationId);
            Baggage.SetBaggage(BaggageCorrelationIdHeader, correlationId);
        }
    }
}