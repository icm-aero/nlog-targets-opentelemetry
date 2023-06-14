using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using NLog;
using NLog.LayoutRenderers;
using OpenTelemetry;

namespace Example.WebApi
{
    /// <summary>
    /// Extension methods for the CorrelationIdMiddleware
    /// </summary>
    public static class CorrelationIdExtensions
    {
        /// <summary>
        /// Enables correlation IDs for the request
        /// </summary>
        /// <param name="app"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseMiddleware<CorrelationIdMiddleware>();
        }

        /// <summary>
        /// Enables correlation IDs for the request
        /// </summary>
        /// <param name="app"></param>
        /// <param name="header">The header field name to use for the correlation ID.</param>
        /// <returns></returns>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app, string header)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            return app.UseCorrelationId(new CorrelationIdOptions
            {
                Header = header
            });
        }

        /// <summary>
        /// Enables correlation IDs for the request
        /// </summary>
        /// <param name="app"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app, CorrelationIdOptions options)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return app.UseMiddleware<CorrelationIdMiddleware>(Options.Create(options));
        }
    }

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

            _next = next; //?? throw new ArgumentNullException(nameof(next));
            _options = options.Value;
        }

        /// <summary>
        /// Processes a request to synchronise TraceIdentifier and Correlation ID headers
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task Invoke(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(_options.Header, out var correlationId) &&
                !string.IsNullOrEmpty(correlationId))
            {
                context.TraceIdentifier = correlationId;
                SetCorrelationId(correlationId);
            }

            if (_options.IncludeInResponse)
            {
                // apply the correlation ID to the response header for client side tracking
                context.Response.OnStarting(() =>
                {
                    var correlationIdString = ServiceTracingContext.GetRequestCorrelationId();
                    if (!string.IsNullOrEmpty(correlationIdString))
                    {
                        context.Response.Headers.Add(_options.Header, new[] { correlationIdString });
                    }
                    return Task.CompletedTask;
                });
            }

            return _next?.Invoke(context) ?? Task.CompletedTask;
        }

        public static string? GetCorrelationId(HttpRequest? context, CorrelationIdOptions options)
        {
            var currentCorrelationId = ServiceTracingContext.GetRequestCorrelationId();
            if (!string.IsNullOrEmpty(currentCorrelationId))
                return currentCorrelationId;

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

            return null;
        }

        public static void SetCorrelationId(string correlationId)
        {
            ServiceTracingContext.SetRequestCorrelationId(correlationId);
            Baggage.SetBaggage(BaggageCorrelationIdHeader, correlationId);
        }
    }

    public class CorrelationIdDelegatingHandler : DelegatingHandler
    {
        private readonly IOptions<CorrelationIdOptions> options;

        public CorrelationIdDelegatingHandler(
            IOptions<CorrelationIdOptions> options)
        {
            this.options = options;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains(options.Value.Header))
            {
                var correlationIdString = ServiceTracingContext.GetRequestCorrelationId();
                request.Headers.Add(options.Value.Header, (string?) correlationIdString);
            }

            // Else the header has already been added due to a retry.
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Options for correlation ids
    /// </summary>
    public class CorrelationIdOptions
    {
        public const string DefaultHeader = "X-Correlation-ID";

        /// <summary>
        /// The header field name where the correlation ID will be stored
        /// </summary>
        public string Header { get; set; } = DefaultHeader;

        /// <summary>
        /// Controls whether the correlation ID is returned in the response headers
        /// </summary>
        public bool IncludeInResponse { get; set; } = true;
    }

    public static class ServiceTracingContext
    {
        const string CorrelationKey = "CorrelationId";

        public static void CreateRequestCorrelationId(bool preserveExisting = true)
        {
            if (preserveExisting && HasCorrelationId()) return;

            CallContext.SetData(CorrelationKey, GenerateId());
        }

        public static bool HasCorrelationId()
        {
            return !string.IsNullOrEmpty(GetRequestCorrelationId());
        }

        public static string GetRequestCorrelationId()
        {
            return (CallContext.GetData(CorrelationKey) as string)!;
        }

        public static void SetRequestCorrelationId(string value)
        {
            CallContext.SetData(CorrelationKey, value);
        }

        private static string GenerateId()
        {
            return Guid.NewGuid().ToString();
        }
    }

    //Taken from: http://www.cazzulino.com/callcontext-netstandard-netcore.html

    /// <summary>
    /// Provides a way to set contextual data that flows with the call and 
    /// async context of a test or invocation.
    /// </summary>
    public static class CallContext
    {
        static readonly ConcurrentDictionary<string, AsyncLocal<object>> State = new();

        /// <summary>
        /// Stores a given object and associates it with the specified name.
        /// </summary>
        /// <param name="name">The name with which to associate the new item in the call context.</param>
        /// <param name="data">The object to store in the call context.</param>
        public static void SetData(string name, object data) =>
            State.GetOrAdd(name, _ => new AsyncLocal<object>()).Value = data;

        /// <summary>
        /// Retrieves an object with the specified name from the <see cref="Example.WebApi.CorrelationId.CallContext"/>.
        /// </summary>
        /// <param name="name">The name of the item in the call context.</param>
        /// <returns>The object in the call context associated with the specified name, or <see langword="null"/> if not found.</returns>
        public static object GetData(string name)
        {
            AsyncLocal<object> data;
            return (State.TryGetValue(name, out data!) ? data.Value : null)!;
        }
    }

    [LayoutRenderer("CorellationId")]
    public class CorrelationIdLayoutRenderer : LayoutRenderer
    {
        public CorrelationIdLayoutRenderer()
        {
        }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var correlationId = ServiceTracingContext.GetRequestCorrelationId();
            builder.Append((string?) correlationId);
        }
    }
}