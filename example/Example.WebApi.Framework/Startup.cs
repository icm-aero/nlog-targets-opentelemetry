using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NLog.Fluent;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using OpenTelemetry;
using Microsoft.OpenApi.Models;

namespace Example.WebApi.Framework
{
    public class Startup
    {
        public const string AspNetEnvironmentVar = "ASPNETCORE_ENVIRONMENT";
        public const string OtelExporterOtlpEndpointVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
        public const string OtelServiceNameVar = "OTEL_SERVICE_NAME";
        public const string OtelServiceVersionVar = "OTEL_SERVICE_VERSION";

        private static NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

        public bool EnableMetrics { get; set; } = true;
        public bool EnableTracing { get; set; } = true;
        public bool AddBaggageToTracing { get; set; } = true;

        public virtual string[] TraceFilterList { get; set; } =
        {
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
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "WebApplication1", Version = "v1" });
            });
            ConfigureOpenTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseDeveloperExceptionPage();
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "WebApplication1 v1"));

            app.UseMvc();
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
            catch (Exception e)
            {
                Log.Error(e, "Error Configuring Open Telemetry");
            }
        }



        protected virtual void AddOpenTelemetryTracing(IServiceCollection services, TracerProviderBuilder builder, ResourceBuilder resourceBuilder)
        {


            var process = Process.GetCurrentProcess();
            var pid = process.Id;
            var processName = process.ProcessName;
            builder
                .SetResourceBuilder(resourceBuilder)
                .AddAspNetCoreInstrumentation(options => 
                    options.EnrichWithHttpRequest = (activity, request) =>
                {
                    activity.AddTag("process.id", pid);
                    activity.AddTag("process.name", processName);

                    if (AddBaggageToTracing)
                        foreach (var baggage in Baggage.Current)
                            activity.SetTag($"baggage.{baggage.Key}", baggage.Value);

                    options.Filter = FilterIncomingTraces;
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.FilterHttpRequestMessage = FilterOutgoingTraces;
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        //var correlationId = ServiceTracingContext.GetRequestCorrelationId();
                        //activity.AddTag("CorellationId", correlationId);
                        activity.AddTag("process.id", pid);
                        activity.AddTag("process.name", processName);

                        if (AddBaggageToTracing)
                            foreach (var baggage in Baggage.Current)
                                activity.SetTag($"baggage.{baggage.Key}", baggage.Value);
                    };
                })
                .AddOtlpExporter();
        }


        protected virtual bool FilterIncomingTraces(HttpContext arg)
        {
            return TraceOperationFilterList.All(s => (arg?.Request?.Path.ToString()!).Contains(s) != true);
        }


#if NET5_0_OR_GREATER
        protected virtual bool FilterOutgoingTraces(HttpRequestMessage request)
        {
            return TraceFilterList.All(s => (request?.RequestUri?.ToString()!).StartsWith(s) != true);
        }
#else
        protected virtual bool FilterOutgoingTraces(HttpRequestMessage request)
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
