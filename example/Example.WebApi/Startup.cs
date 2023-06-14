using NLog.Fluent;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using Example.WebApi.CorrelationId;
using OpenTelemetry;
using Microsoft.OpenApi.Models;
using System.Security.Cryptography;

namespace Example.WebApi
{
    public class Startup
    {
        public const string AspNetEnvironmentVar = "ASPNETCORE_ENVIRONMENT";
        public const string OtelExporterOtlpEndpointVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string OtelLogExporterOtlpEndpointVar = "OTEL_LOG_EXPORTER_OTLP_ENDPOINT";
        public const string OtelServiceNameVar = "OTEL_SERVICE_NAME";
        public const string OtelServiceVersionVar = "OTEL_SERVICE_VERSION";

        private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        public bool EnableMetrics { get; set; } = true;
        public bool EnableTracing { get; set; } = true;
        public bool AddBaggageToTracing { get; set; } = true;

        public virtual string[] ExcludedBaggage { get; set; } =
        {
            CorrelationIdMiddleware.BaggageCorrelationIdHeader
        };

        public virtual string[] TraceFilterList { get; set; } =
        {
            Environment.GetEnvironmentVariable(OtelExporterOtlpEndpointVar)!
        };
        public virtual string[] TraceOperationFilterList { get; set; } =
        {
            "/health", "/livez", "/readyz", "health/ready", "health/live"
        };

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<CorrelationIdDelegatingHandler>();

            //services.AddRazorPages();
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "WebApplication1", Version = "v1" });
            });
            ConfigureOpenTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCorrelationId(new CorrelationIdOptions());

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebApplication1 v1"));
            }


            app.UseStaticFiles();

            app.UseRouting();


            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }

       

        protected virtual void ConfigureOpenTelemetry(IServiceCollection services)
        {
            try
            {
                var environmentName = Environment.GetEnvironmentVariable(AspNetEnvironmentVar);
                var apmEndpoint = Environment.GetEnvironmentVariable(OtelExporterOtlpEndpointVar);
                var serviceName = Environment.GetEnvironmentVariable(OtelServiceNameVar);
                var serviceVersion = Environment.GetEnvironmentVariable(OtelServiceVersionVar);

                Log.Info("Enabling Open Telemetry: {0}", apmEndpoint);

                if (string.IsNullOrEmpty(serviceName))
                {
                    Log.Error("Error retrieving service name, skipping Open Telemetry Configuration");
                    return;
                }

                var resourceBuilder = ResourceBuilder
                    .CreateDefault()
                    .AddTelemetrySdk()
                    .AddAttributes(new[]
                    {
                        new KeyValuePair<string, object>("deployment.environment", environmentName!)
                    })
                    .AddService(serviceName, "MS", serviceVersion, false);

                if (EnableTracing)
                {
                    Log.Info("Enabling Open Telemetry Tracing: {0}", apmEndpoint);
                    services.AddOpenTelemetryTracing(builder =>
                        AddOpenTelemetryTracing(services, builder, resourceBuilder));
                }

                if (EnableMetrics)
                {
                    Log.Info("Enabling Open Telemetry Metrics: {0}", apmEndpoint);
                    services.AddOpenTelemetryMetrics(builder =>
                        AddOpenTelemetryMetrics(services, builder, resourceBuilder));
                }

            }
            catch (Exception e)
            {
                Log.Error(e, "Error Configuring Open Telemetry");
            }
        }

      

        protected virtual void AddOpenTelemetryTracing(IServiceCollection services, TracerProviderBuilder builder, ResourceBuilder resourceBuilder)
        {
            //DiagnosticListener.AllListeners.Subscribe(new TestDiagnosticObserver());
            var correlationIdOptions = new CorrelationIdOptions();

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
                        if (eventName != "OnStartActivity") return;

                        var httpRequest = eventObject as HttpRequest;
                        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpRequest, correlationIdOptions);
                        AddBaggageToTracingTags(activity, correlationId!, processName, pid.ToString());
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.Filter = FilterOutgoingTraces;
                    options.Enrich = (activity, eventName, eventObject) =>
                    {
                        if (eventName != "OnStartActivity") return;

                        var correlationId = ServiceTracingContext.GetRequestCorrelationId();
                        AddBaggageToTracingTags(activity, correlationId!, processName, pid.ToString());
                    };
                })
                .AddOtlpExporter();
        }

        protected virtual void AddBaggageToTracingTags(Activity activity, string correlationId, string processName, string processId)
        {
            activity.AddTag("correlationId", correlationId);
            activity.AddTag("process.id", processId);
            activity.AddTag("process.name", processName);

            if (!AddBaggageToTracing) return;

            var currentBaggage = Baggage.Current.GetBaggage()
                .Where(pair => !ExcludedBaggage.Contains(pair.Key));

            foreach (var baggage in currentBaggage)
                activity.SetTag($"baggage.{baggage.Key}", baggage.Value);
        }


        protected virtual bool FilterIncomingTraces(HttpContext arg)
        {
            return TraceOperationFilterList.All(s => (arg?.Request?.Path.ToString()!).Contains(s) != true);
        }


#if NET5_0_OR_GREATER
        protected virtual bool FilterOutgoingTraces(HttpRequestMessage request)
        {
            var filterList = TraceFilterList.Where(s => !string.IsNullOrEmpty(s));
            
            var logEndpoint = Environment.GetEnvironmentVariable(OtelLogExporterOtlpEndpointVar);
            if(!string.IsNullOrEmpty(logEndpoint)) filterList = filterList.Append(logEndpoint);

            return filterList.All(s => (request?.RequestUri?.ToString()!).StartsWith(s) != true);
        }
#else
        protected virtual bool FilterOutgoingTraces(HttpWebRequest request)
        {
            return TraceFilterList.All(s => request?.RequestUri?.ToString().StartsWith(s) != true);
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