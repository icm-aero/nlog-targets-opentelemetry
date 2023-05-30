using System.Diagnostics;
using System.Reflection;
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
        /// Gets or sets whether to include all baggage of the log event in the document
        /// </summary>
        public bool IncludeBaggage { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include all activity tags of the log event in the document
        /// </summary>
        public bool IncludeTags { get; set; } = true;


        /// <summary>
        /// Gets or sets a list of additional resource attributes to add to the elasticsearch document.
        /// </summary>
        [ArrayParameter(typeof(Serilog.NLog.Attribute), "contextproperty")]
        public IList<TargetPropertyWithContext> ResourceAttributes { get; set; } = new List<TargetPropertyWithContext>();

        /// <summary>
        /// <inheritdoc cref="OtelTarget.IncludeDefaultFields"/>
        /// </summary>
        public bool IncludeDefaultFields { get; set; } = true;

        public bool IncludeProcessInfo { get; set; } = true;
        public bool IncludeThreadInfo { get; set; } = true;

        public bool IncludeClassInfo { get; set; } = true;

        public bool IncludeMessageTemplateText { get; set; } = false;

        public bool IncludeMessageTemplateMD5Hash { get; set; } = false;


        //private readonly SimpleLogRecordExportProcessor exporter;
        private IExporter grpcExporter;

        private ExportLogsServiceRequest _requestTemplate;
        

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

        private List<KeyValue> _defaultAttributes = new List<KeyValue>();

        //private OtelPropertyConvertor _otelPropertyConvertor = new OtelPropertyConvertor();
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
                InitializeDefaultAttributes(eventInfo);
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

                if (_sw == null)
                {
                    _sw = new Stopwatch();
                    _sw.Start();
                }
                grpcExporter.Export(request);
                Console.WriteLine($"{_counter++}:{request.ResourceLogs[0].ScopeLogs[0].LogRecords.Count} ({_sw.Elapsed.TotalSeconds})");
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error writing logEvents to OTEL");
                throw;
            }
        }

        private int _counter = 0;
        private Stopwatch? _sw;
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

                ProcessProperties(logRecord, logEvent);
                ProcessTraceContext(logRecord);
                ProcessDefaultAttributes(logRecord, logEvent);
                ProcessBaggageAndTags(logRecord);
                ProcessMessageTemplate(logRecord, logEvent);
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

        private void InitializeDefaultAttributes(LogEventInfo eventInfo)
        {
            if (IncludeProcessInfo)
            {
                _defaultAttributes.Add(AddAttributeFromLayout(eventInfo, "process.name", "${processname}"));
                _defaultAttributes.Add(AddAttributeFromLayout(eventInfo, "process.id", "${processid}"));
            }
        }

        private KeyValue AddAttributeFromLayout(LogEventInfo eventInfo, string name, string layout)
        {
            var processLayout = Layout.FromString(layout);
            var processName = processLayout.Render(eventInfo);
            var processNameAttribute = PrimitiveConversions.NewAttribute(name,
                PrimitiveConversions.ToOpenTelemetryScalar(processName));
            return processNameAttribute;
        }

        private void ProcessDefaultAttributes(LogRecord logRecord, LogEventInfo logEvent)
        {
            if (logEvent.LoggerName != null)
                logRecord.Attributes.Add(PrimitiveConversions.NewAttribute("logger",
                    PrimitiveConversions.ToOpenTelemetryScalar(logEvent.LoggerName)));

            logRecord.Attributes.Add(PrimitiveConversions.NewAttribute("ddsource",
                PrimitiveConversions.ToOpenTelemetryScalar("csharp")));

            logRecord.Attributes.Add(_defaultAttributes);

            if (IncludeCallSite)
            {
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "code.class", logEvent.CallerClassName));
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "code.method", logEvent.CallerMemberName));
            }

            if (IncludeThreadInfo)
            {
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "thread.name", "${threadname}"));
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "thread.id", "${threadid}"));
            }
        }

        

        private void ProcessBaggageAndTags(LogRecord logRecord)
        {
            try
            {
                if (IncludeBaggage)
                {
                    var activityBaggage = Activity.Current?.Baggage
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                        .Where(baggageItem => baggageItem.Value != null);

                    if (activityBaggage == null) return;

                    foreach (var baggageItem in activityBaggage)
                    {
                        logRecord.Attributes.Add(
                            PrimitiveConversions.NewStringAttribute($"baggage.{baggageItem.Key}",
                                baggageItem.Value!));
                    }
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Baggage");
            }

            try
            {
                if (IncludeTags)
                {
                    var tags = Activity.Current?.Tags
                        .ToDictionary(pair => pair.Key, pair => pair.Value)
                        .Where(baggageItem => baggageItem.Value != null);

                    if (tags == null) return;

                    foreach (var tagItem in tags)
                    {
                        logRecord.Attributes.Add(
                            PrimitiveConversions.NewStringAttribute($"tags.{tagItem.Key}",
                                tagItem.Value!));
                    }
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Tags");
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
                var p = GetAllProperties(logEvent);
                foreach (var property in p)
                {
                    var propertyKey = property.Key;
                    if (propertyKey == null) continue;

                    //var isPrimitive = property.Value.GetType().;
                    //var data = isPrimitive ? property.Value.ToString() : JsonConvert.SerializeObject(property.Value);//
                    //logRecord.Attributes.Add(PrimitiveConversions.NewStringAttribute($"{propertyKey}", data));

                    var data = PrimitiveConversions.ToSerializedOpenTelemetryPrimitive(property.Value);
                    logRecord.Attributes.Add(PrimitiveConversions.NewAttribute($"{propertyKey}", data));

                    //    // Uncomment to use Serilog OTEL Object Convertor
                    //    // ----------------------------------------------------------------------------------------
                    //    //var v = _otelPropertyConvertor.ConvertObjectToAnyValue(property.Value);
                    //    //logRecord.Attributes.Add(PrimitiveConversions.NewAttribute($"{propertyKey}", v));
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
