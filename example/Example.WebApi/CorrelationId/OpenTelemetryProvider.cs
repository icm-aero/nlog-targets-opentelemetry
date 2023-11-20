#pragma warning disable CS8600
#pragma warning disable CS8603
#pragma warning disable CS8601
#pragma warning disable CS8618
#pragma warning disable CS8625
#pragma warning disable CS8604
#pragma warning disable CS8602

using Jedi.ServiceFabric.Tracing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using Jedi.ServiceFabric.AspNetCore.Correlation;
using NLog;
using OpenTelemetry;

namespace Jedi.ServiceFabric.AspNetCore.OpenTelemetry
{
    public class OpenTelemetryProvider
    {
        private static Logger Log = LogManager.GetCurrentClassLogger();
        public string ServiceName { get; set; }
        public string ServiceVersion { get; set; }
        public string EnvironmentName { get; set; }
        

        public const string AspNetEnvironmentVar = "ASPNETCORE_ENVIRONMENT";
        public const string OtelExporterOtlpEndpointVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string OtelLogExporterOtlpEndpointVar = "OTEL_LOG_EXPORTER_OTLP_ENDPOINT";
        public const string OtelServiceNameVar = "OTEL_SERVICE_NAME";
        public const string OtelServiceVersionVar = "OTEL_SERVICE_VERSION";
        
        public CorrelationIdOptions CorrelationIdOptions { get; }

        public virtual bool EnableOpenTelemetry { get; set; } = true;
        public virtual bool EnableTracing { get; set; } = true;
        public virtual bool EnableMetrics { get; set; } = false;

        public virtual bool AddBaggageToTracing { get; set; } = true;

        public virtual string[] ExcludedBaggage { get; set; } =
        {
            CorrelationIdMiddleware.BaggageCorrelationIdHeader
        };

        public virtual string[] TraceFilterList { get; set; } =
        {
            Environment.GetEnvironmentVariable(OtelExporterOtlpEndpointVar),
            Environment.GetEnvironmentVariable("ELASTIC_SEARCH_ENDPOINT")
        };

        public virtual string[] TraceOperationFilterList { get; set; } =
        {
            "/health", "/livez", "/readyz", "health/ready", "health/live"
        };

        protected readonly string Pid;
        protected readonly string ProcessName;


        public OpenTelemetryProvider(CorrelationIdOptions correlationIdOptions = null)
        {
            try
            {
                CorrelationIdOptions = correlationIdOptions ?? new CorrelationIdOptions();

                var process = Process.GetCurrentProcess();
                Pid = process.Id.ToString();
                ProcessName = process.ProcessName;
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in OpenTelemetryProvider Constructor");
            }
        }

        public virtual void ConfigureOpenTelemetry(IServiceCollection services, string serviceName = null, string serviceVersion = null, string environmentName = null)
        {
            try
            {
                ServiceName = serviceName ?? Environment.GetEnvironmentVariable(OtelServiceNameVar);
                ServiceVersion = serviceVersion ?? Environment.GetEnvironmentVariable(OtelServiceVersionVar);
                EnvironmentName = environmentName ?? Environment.GetEnvironmentVariable(AspNetEnvironmentVar);
                if (string.IsNullOrEmpty(EnvironmentName))
                    EnvironmentName = "unknown";
                if (string.IsNullOrEmpty(ServiceVersion))
                    ServiceVersion = "unknown";

                if (string.IsNullOrEmpty(ServiceName))
                    throw new ArgumentException("Invalid Service Name", nameof(ServiceName));
 
                var apmEndpoint = Environment.GetEnvironmentVariable(OtelExporterOtlpEndpointVar);

                if (EnableOpenTelemetry
                    && !string.IsNullOrEmpty(apmEndpoint))
                {
                    Log.Info("Enabling Open Telemetry: {0}", apmEndpoint);

                    var resourceBuilder = ResourceBuilder
                        .CreateDefault()
                        .AddTelemetrySdk()
                        .AddAttributes(new[]
                        {
                            new KeyValuePair<string, object>("deployment.environment", EnvironmentName)
                        })
                        .AddService(ServiceName, "MS", ServiceVersion, false);

                    if (EnableTracing)
                    {
                        Log.Info("Enabling Open Telemetry Tracing: {0}", apmEndpoint);
                        services.AddOpenTelemetry()
                            .WithTracing(builder =>
                                AddOpenTelemetryTracing(services, builder, resourceBuilder));
                    }

                    if (EnableMetrics)
                    {
                        Log.Info("Enabling Open Telemetry Metrics: {0}", apmEndpoint);
                        services.AddOpenTelemetry().WithMetrics(builder =>
                            AddOpenTelemetryMetrics(services, builder, resourceBuilder));
                    }
                }
                else
                {
                    Log.Info("Open Telemetry Disabled");
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error Configuring Open Telemetry");
                EnableOpenTelemetry = false;
            }
        }

        protected virtual void AddOpenTelemetryTracing(IServiceCollection services, TracerProviderBuilder builder, ResourceBuilder resourceBuilder)
        {
            builder
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = FilterIncomingTraces;
                    options.EnrichWithHttpRequest = (activity, httpRequest) =>
                    {
                        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpRequest, CorrelationIdOptions);
                        AddBaggageToTracingTags(activity, correlationId);
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.FilterHttpRequestMessage = FilterOutgoingTraces;
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        var correlationId = ServiceTracingContext.GetRequestCorrelationId();
                        AddBaggageToTracingTags(activity, correlationId);
                    };
                })
                .AddOtlpExporter();
        }

        protected virtual void AddBaggageToTracingTags(Activity activity, string correlationId)
        {
            activity.AddTag("correlationId", correlationId);
            activity.AddTag("process.id", Pid);
            activity.AddTag("process.name", ProcessName);

            if (!AddBaggageToTracing) return;

            var currentBaggage = Baggage.Current.GetBaggage()
                .Where(pair => !ExcludedBaggage.Contains(pair.Key));

            foreach (var baggage in currentBaggage)
                activity.SetTag($"baggage.{baggage.Key}", baggage.Value);
        }

        protected virtual bool FilterIncomingTraces(HttpContext arg)
        {
            return TraceOperationFilterList.All(s => (arg?.Request?.Path.ToString()).Contains(s) != true);
        }

#if NET5_0_OR_GREATER
        protected virtual bool FilterOutgoingTraces(HttpRequestMessage request)
        {
            var filterList = TraceFilterList.Where(s => !string.IsNullOrEmpty(s));

            var logEndpoint = Environment.GetEnvironmentVariable(OtelLogExporterOtlpEndpointVar);
            if (!string.IsNullOrEmpty(logEndpoint)) filterList = filterList.Append(logEndpoint);

            return filterList.All(s => request?.RequestUri?.ToString()?.StartsWith(s) != true);
        }
#else
        protected virtual bool FilterOutgoingTraces(HttpWebRequest request)
        {
            var filterList = TraceFilterList.Where(s => !string.IsNullOrEmpty(s));

            var logEndpoint = Environment.GetEnvironmentVariable(OtelLogExporterOtlpEndpointVar);
            if (!string.IsNullOrEmpty(logEndpoint)) filterList = filterList.Append(logEndpoint);

            return filterList.All(s => request?.RequestUri?.ToString()?.StartsWith(s) != true);
        }
#endif

        protected virtual void AddOpenTelemetryMetrics(IServiceCollection services, MeterProviderBuilder builder, ResourceBuilder resourceBuilder)
        {
            builder
                .SetResourceBuilder(resourceBuilder)
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation()
                //.AddMeter("icm.ms.service")
                .AddOtlpExporter();
        }
    }
}
