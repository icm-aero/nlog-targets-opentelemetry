using System.Diagnostics;
using System.Reflection;
using NLog;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Example.WebApi.CorrelationId
{
    public class OpenTelemetryModule
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public const string EnvOtelServiceName = "OTEL_SERVICE_NAME";
        public const string EnvOtelServiceVersion = "OTEL_SERVICE_VERSION";
        public const string EnvOtelEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string EnvDeploymentEnvName = "ASPNETCORE_ENVIRONMENT";

        public string ServiceName { get; }
        public string ServiceVersion { get; }
        public string EnvironmentName { get; }
        public string ApmEndpoint { get; }

        public bool EnableOpenTelemetry { get; set; } = true;
        public bool EnableTracing { get; set; } = true;
        public bool EnableMetrics { get; set; } = true;
        public bool AddBaggageToTracing { get; set; } = true;

        public string[] TraceFilterList { get; set; } =
        {
            Environment.GetEnvironmentVariable("ELASTIC_SEARCH_ENDPOINT")!,
            Environment.GetEnvironmentVariable(EnvOtelEndpoint)!
        };
        public string[] TraceOperationFilterList { get; set; } =
        {
            "/health"
        };

        public OpenTelemetryModule(string serviceName = null!, string serviceVersion = null!, string environmentName = null!)
        {
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;

            ServiceName = (serviceName ?? Environment.GetEnvironmentVariable(EnvOtelServiceName))!;
            ServiceVersion = (serviceVersion ?? Environment.GetEnvironmentVariable(EnvOtelServiceVersion))!;
            if (string.IsNullOrEmpty(ServiceVersion)) ServiceVersion = assemblyVersion?.ToString() ?? "unkown";

            EnvironmentName = (environmentName ?? Environment.GetEnvironmentVariable(EnvDeploymentEnvName))!;
            ApmEndpoint = Environment.GetEnvironmentVariable(EnvOtelEndpoint)!;

            CheckConfig("Service Name", ServiceName);
            CheckConfig("Service Version", ServiceVersion);
            CheckConfig("OTEL Endpoint", ApmEndpoint);
            CheckConfig("Environment Name", EnvironmentName);

            GlobalDiagnosticsContext.Set("ServiceName", ServiceName);
            GlobalDiagnosticsContext.Set("ServiceVersion", serviceVersion);

            Log.Info("Initializing OTEL Module");
        }

        private void CheckConfig(string name, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Log.Error("Error Configuring Open Telemetry: Missing Parameter: {0}", name);
                EnableOpenTelemetry = false;
            }
        }

        public void ConfigureOpenTelemetry(IServiceCollection services)
        {
            try
            {
                if (!EnableOpenTelemetry)
                {
                    Log.Info("Open Telemetry Disabled");
                    return;
                }

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
                    Log.Info("Enabling Open Telemetry Tracing: {0}", ApmEndpoint);
                    services.AddOpenTelemetryTracing(builder =>
                        AddOpenTelemetryTracing(builder, resourceBuilder));
                }

                if (EnableMetrics)
                {
                    Log.Info("Enabling Open Telemetry Metrics: {0}", ApmEndpoint);
                    services.AddOpenTelemetryMetrics(builder =>
                        AddOpenTelemetryMetrics(builder));
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Error Configuring Open Telemetry");
            }
        }

        protected void AddOpenTelemetryTracing(TracerProviderBuilder builder, ResourceBuilder resourceBuilder)
        {
            var process = Process.GetCurrentProcess();
            var pid = process.Id;
            var processName = process.ProcessName;
            builder
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = FilterIncomingTraces;
                    options.Enrich = (activity, eventName, eventObject) =>
                    {
                        var correlationId = ServiceTracingContext.GetRequestCorrelationId();
                        activity.AddTag("correlationId", correlationId);
                        activity.AddTag("process.id", pid);
                        activity.AddTag("process.name", processName);

                        if (AddBaggageToTracing)
                            foreach (var baggage in Baggage.Current)
                                activity.SetTag($"baggage.{baggage.Key}", baggage.Value);
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.Filter = FilterOutgoingTraces;
                    options.Enrich = (activity, eventName, eventObject) =>
                    {
                        var correlationId = ServiceTracingContext.GetRequestCorrelationId();
                        activity.AddTag("correlationId", correlationId);
                        activity.AddTag("process.id", pid);
                        activity.AddTag("process.name", processName);

                        if (AddBaggageToTracing)
                            foreach (var baggage in Baggage.Current)
                                activity.SetTag($"baggage.{baggage.Key}", baggage.Value);
                    };
                })
                .AddOtlpExporter();
        }

        protected bool FilterIncomingTraces(HttpContext arg)
        {
            return TraceOperationFilterList.All(s => (arg?.Request?.Path.ToString()!).Contains(s) != true);
        }

        protected bool FilterOutgoingTraces(HttpRequestMessage request)
        {
            return TraceFilterList.All(s => (request?.RequestUri?.ToString()!).StartsWith(s) != true);
        }

        protected void AddOpenTelemetryMetrics(MeterProviderBuilder builder)
        {
            builder
                .AddMeter("icm.ms.service")
                .AddHttpClientInstrumentation()
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter();
        }
    }
}
