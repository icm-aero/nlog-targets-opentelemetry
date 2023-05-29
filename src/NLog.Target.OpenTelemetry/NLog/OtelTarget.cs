using System.Diagnostics;
using Newtonsoft.Json;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Trace;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Sinks.OpenTelemetry.ProtocolHelpers;

#pragma warning disable CS8618
#pragma warning disable CS1591

namespace NLog.OpenTelemetry
{
    [Target("Otel")]
    public sealed class OtelTarget : TargetWithContext
    {
        public const string DefaultEnvOtelServiceName = "OTEL_SERVICE_NAME";

        public const string DefaultEnvOtelServiceVersion = "OTEL_SERVICE_VERSION";

        public const string DefaultEnvDeploymentEnvName = "ASPNETCORE_ENVIRONMENT";

        public const string DefaultEnvOtelExporterOtlpProtocol = "OTEL_EXPORTER_OTLP_PROTOCOL";

        public const string DefaultEnvOtelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";

        public const string DefaultEnvOtelExporterOtlpHeaders = "OTEL_EXPORTER_OTLP_HEADERS";

        public Layout ServiceName { get; set; }

        public Layout ServiceVersion { get; set; }

        public Layout Environment { get; set; }

        public Layout OtlpEndpoint { get; set; }

        public Layout OtlpProtocol { get; set; }

        public Layout OtlpHeaders { get; set; }

        /// <summary>
        /// Gets or sets whether to include all properties of the log event in the document
        /// </summary>
        public bool IncludeAllProperties { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include all baggage of the log event in the document
        /// </summary>
        public bool IncludeBaggage { get; set; } = true;

        /// <summary>
        /// Gets or sets a comma separated list of excluded properties when setting <see cref="OtelTarget.IncludeAllProperties"/>
        /// </summary>
        public string ExcludedProperties { get; set; }

        /// <summary>
        /// Gets or sets a list of additional fields to add to the elasticsearch document.
        /// </summary>
        [ArrayParameter(typeof(Serilog.NLog.Attribute), "attribute")]
        public IList<Serilog.NLog.Attribute> Attributes { get; set; } = new List<Serilog.NLog.Attribute>();

        /// <summary>
        /// Gets or sets a list of additional resource attributes to add to the elasticsearch document.
        /// </summary>
        [ArrayParameter(typeof(Serilog.NLog.Attribute), "attribute")]
        public IList<Serilog.NLog.Attribute> ResourceAttributes { get; set; } = new List<Serilog.NLog.Attribute>();

        /// <summary>
        /// <inheritdoc cref="OtelTarget.IncludeDefaultFields"/>
        /// </summary>
        public bool IncludeDefaultFields { get; set; } = true;

        public bool IncludeMessageTemplateText { get; set; } = false;

        public bool IncludeMessageTemplateMD5Hash { get; set; } = false;


        //private readonly SimpleLogRecordExportProcessor exporter;
        private IExporter grpcExporter;

        private ExportLogsServiceRequest _requestTemplate;
        private HashSet<string> _excludedProperties;
        

        /// <summary>
        /// A https://messagetemplates.org template, as text. For example, the string <c>Hello {Name}!</c>.
        /// </summary>
        /// <remarks>See also https://opentelemetry.io/docs/reference/specification/logs/semantic_conventions/ and
        /// <see cref="TraceSemanticConventions"/>.</remarks>
        public const string AttributeMessageTemplateText = "message_template.text";

        /// <summary>
        /// A https://messagetemplates.org template, hashed using MD5 and encoded as a 128-bit hexadecimal value.
        /// </summary>
        /// <remarks>See also https://opentelemetry.io/docs/reference/specification/logs/semantic_conventions/ and
        /// <see cref="TraceSemanticConventions"/>.</remarks>
        public const string AttributeMessageTemplateMD5Hash = "message_template.hash.md5";

        private OtelPropertyConvertor _otelPropertyConvertor = new OtelPropertyConvertor();
        public OtelTarget()
        {
            CreateSerilogPropertyValueConverter();
        }

        private void CreateSerilogPropertyValueConverter()
        {
            
        }

        protected override void InitializeTarget()
        {
            base.InitializeTarget();

            try
            {
                if (!string.IsNullOrEmpty(ExcludedProperties))
                    _excludedProperties =
                        new HashSet<string>(
                            ExcludedProperties.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries));

                Layout = Layout.FromString("${message:withexception=false}");

                var eventInfo = LogEventInfo.CreateNullEvent();

                var otlpProtocol = GetProperty(eventInfo, OtlpProtocol, DefaultEnvOtelExporterOtlpProtocol, "grpc");
                var otlpEndpoint = GetProperty(eventInfo, OtlpEndpoint, DefaultEnvOtelExporterOtlpEndpoint, "http://localhost:4317");
                if (!otlpEndpoint.EndsWith("/")) otlpEndpoint += "/";

                var otlpHeaders = GetHeaders(eventInfo);

                grpcExporter = otlpProtocol switch
                {
                    "grpc" => new GrpcExporter($"{otlpEndpoint}v1/logs", otlpHeaders),
                    "http" => new HttpExporter($"{otlpEndpoint}v1/logs", otlpHeaders),
                    _ => throw new ArgumentException("Invalid OTLP Protocol, supported options: grpc, http")
                };

                CreateRequestTemplate(eventInfo);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error initializing OTEL Target");
                throw;
            }
        }

        private string GetProperty(LogEventInfo eventInfo, Layout layout, string environmentVariable, string defaultValue)
        {
            return layout?.Render(eventInfo) ??
                   System.Environment.GetEnvironmentVariable(environmentVariable) ??
                   defaultValue;
        }

        private Dictionary<string, string> GetHeaders(LogEventInfo eventInfo)
        {
            try
            {
                var otlpHeaders = new Dictionary<string, string>();
                var otlpHeadersString = OtlpHeaders?.Render(eventInfo) ??
                                        System.Environment.GetEnvironmentVariable(DefaultEnvOtelExporterOtlpHeaders);
                if (!string.IsNullOrEmpty(otlpHeadersString))
                {
                    var keyValuePairs = otlpHeadersString.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var keyValue in keyValuePairs)
                    {
                        var pair = keyValue.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                        if (pair.Length == 2)
                        {
                            otlpHeaders[pair[0].Trim()] = pair[1].Trim();
                        }
                    }
                }

                return otlpHeaders;
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error parsing OTEL Headers");
                throw;
            }
        }

        private void CreateRequestTemplate(LogEventInfo eventInfo)
        {
            try
            {
                var serviceName = ServiceName?.Render(eventInfo) ??
                                  System.Environment.GetEnvironmentVariable(DefaultEnvOtelServiceName);
                var serviceVersion = ServiceVersion?.Render(eventInfo) ??
                                     System.Environment.GetEnvironmentVariable(DefaultEnvOtelServiceVersion);
                var environment = Environment?.Render(eventInfo) ??
                                  System.Environment.GetEnvironmentVariable(DefaultEnvDeploymentEnvName);

                var fullSemVer = GitVersionInformation.FullSemVer;
                var resources = new Dictionary<string, object>
                {
                    ["telemetry.sdk.name"] = "nlog.targets.otel",
                    ["telemetry.sdk.language"] = "csharp",
                    ["telemetry.sdk.version"] = fullSemVer,
                    ["ddsource"] = "csharp"
                };
                if (serviceName != null) resources.Add("service.name", serviceName);
                if (serviceVersion != null) resources.Add("service.version", serviceVersion);
                if (environment != null) resources.Add("deployment.environment", environment);

                foreach (var attribute in ResourceAttributes)
                {
                    var value = attribute.Layout.Render(eventInfo);
                    resources.Add(attribute.Name, value);
                }
                _requestTemplate = RequestTemplateFactory.CreateRequestTemplate(resources);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error creating OTEL request template");
                throw;
            }
        }

        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            try
            {
                var request = _requestTemplate.Clone();

                foreach (var asyncLogEvent in logEvents)
                {
                    WriteLogEntry(request, asyncLogEvent);
                }

                grpcExporter.Export(request);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error writing logEvents to OTEL");
                throw;
            }
        }

        private void WriteLogEntry(ExportLogsServiceRequest request, AsyncLogEventInfo asyncLogEvent)
        {
            try
            {
                var logEvent = asyncLogEvent.LogEvent;
                string logMessage = Layout.Render(logEvent);

                var logRecord = new LogRecord
                {
                    Body = new AnyValue { StringValue = logMessage },
                    TimeUnixNano = PrimitiveConversions.ToUnixNano(logEvent.TimeStamp),
                    SeverityText = logEvent.Level.ToString(),
                    SeverityNumber = PrimitiveConversions.ToSeverityNumber(logEvent.Level),
                    Attributes = { }
                };

                ProcessTraceContext(logRecord);
                ProcessAttributes(logRecord, logEvent);
                ProcessBaggage(logRecord);
                ProcessMessageTemplate(logRecord, logEvent);
                ProcessProperties(logRecord, logEvent);
                ProcessException(logRecord, logEvent);

                request.ResourceLogs[0].ScopeLogs[0].LogRecords.Add(logRecord);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error writing logEvent to OTEL");
            }
            
        }

        private void ProcessTraceContext(LogRecord logRecord)
        {
            try
            {
                var traceId = Activity.Current?.TraceId.ToHexString();
                if (traceId != null) logRecord.TraceId = PrimitiveConversions.ToOpenTelemetryTraceId(traceId);

                var spanId = Activity.Current?.SpanId.ToHexString();
                if (spanId != null) logRecord.SpanId = PrimitiveConversions.ToOpenTelemetrySpanId(spanId);

            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Trace Context");
            }
        }

        private void ProcessAttributes(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                if (logEvent.LoggerName != null)
                    logRecord.Attributes.Add(PrimitiveConversions.NewAttribute("logger",
                        PrimitiveConversions.ToOpenTelemetryScalar(logEvent.LoggerName)));

                foreach (var attribute in Attributes)
                {
                    var renderedField = RenderLogEvent(attribute.Layout, logEvent);
                    var v = PrimitiveConversions.ToOpenTelemetryScalar(renderedField);
                    if (!string.IsNullOrEmpty(renderedField))
                    {
                        logRecord.Attributes.Add(PrimitiveConversions.NewAttribute(attribute.Name, v));
                    }
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Attributes");
                throw;
            }
        }

        private void ProcessBaggage(LogRecord logRecord)
        {
            try
            {
                if (!IncludeBaggage) return;

                var activityBaggage = Activity.Current?.Baggage
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
                    .Where(baggageItem => baggageItem.Value != null);

                foreach (var baggageItem in activityBaggage!)
                {
                    logRecord.Attributes.Add(
                        PrimitiveConversions.NewStringAttribute($"baggage.{baggageItem.Key}",
                            baggageItem.Value!));
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Baggage");
            }
        }

        private void ProcessMessageTemplate(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                if (IncludeMessageTemplateText)
                {
                    logRecord.Attributes.Add(PrimitiveConversions.NewAttribute(AttributeMessageTemplateText, new()
                    {
                        StringValue = logEvent.Message
                    }));
                }

                if (IncludeMessageTemplateMD5Hash)
                {
                    logRecord.Attributes.Add(PrimitiveConversions.NewAttribute(AttributeMessageTemplateMD5Hash,
                        new AnyValue
                        {
                            StringValue = PrimitiveConversions.Md5Hash(logEvent.Message)
                        }));
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Message Template");
            }
        }

        private void ProcessProperties(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                if (!logEvent.HasProperties || !IncludeAllProperties) return;

                foreach (var property in logEvent.Properties)
                {
                    var propertyKey = property.Key.ToString();
                    if (propertyKey == null) continue;

                    if (_excludedProperties?.Contains(propertyKey) == true)
                        continue;

                    // Uncomment to use Serilog OTEL Object Convertor
                    // ----------------------------------------------------------------------------------------
                    //var v = _otelPropertyConvertor.ConvertObjectToAnyValue(property.Value);
                    //logRecord.Attributes.Add(PrimitiveConversions.NewAttribute($"{propertyKey}", v));

                    var data = JsonConvert.SerializeObject(property.Value);
                    logRecord.Attributes.Add(PrimitiveConversions.NewStringAttribute($"{propertyKey}", data));
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing All Properties");
            }
        }

        private void ProcessException(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                var ex = logEvent.Exception;
                if (ex == null) return;

                var attrs = logRecord.Attributes;
                attrs.Add(PrimitiveConversions.NewStringAttribute(TraceSemanticConventions.AttributeExceptionType, ex.GetType().ToString()));

                if (ex.Message != "")
                    attrs.Add(PrimitiveConversions.NewStringAttribute(TraceSemanticConventions.AttributeExceptionMessage, ex.Message));

                if (ex.ToString() != "")
                    attrs.Add(PrimitiveConversions.NewStringAttribute(TraceSemanticConventions.AttributeExceptionStacktrace, ex.ToString()));
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Exception");
                throw;
            }
        }
    }
}
