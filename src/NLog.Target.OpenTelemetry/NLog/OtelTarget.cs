using System.Collections.Concurrent;
using Google.Protobuf.Collections;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.OpenTelemetry.NLog;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
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
        
        public const string DefaultEnvOtelLogExporterOtlpEndpoint = "OTEL_LOG_EXPORTER_OTLP_ENDPOINT";

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
        /// Gets or sets whether to include the correlation id of the log event in the document
        /// </summary>
        public bool IncludeCorrelationId { get; set; } = true;


        /// <summary>
        /// Gets or sets a list of additional resource attributes to add to the elasticsearch document.
        /// </summary>
        [ArrayParameter(typeof(TargetPropertyWithContext), "contextproperty")]
        public IList<TargetPropertyWithContext> ResourceAttributes { get; set; } =
            new List<TargetPropertyWithContext>();

        public bool IncludeProcessInfo { get; set; } = true;
        public bool IncludeThreadInfo { get; set; } = true;

        public bool IncludeMessageTemplateText { get; set; } = false;

        public bool IncludeMessageTemplateMD5Hash { get; set; } = false;

        //private readonly SimpleLogRecordExportProcessor exporter;
        private IExporter grpcExporter;

        private ExportLogsServiceRequest _requestTemplate;

        private readonly ConcurrentDictionary<string, Layout> _resourceAttributes = new();

        /// <summary>
        /// Gets or sets a comma separated list of excluded tags />
        /// </summary>
        public string ExcludedTags { get; set; }

        private HashSet<string> _excludedTags = new(new[]
        {
            CorrelationIdTag,
            "process.id",
            "process.name"
        });

        /// <summary>
        /// Gets or sets a comma separated list of excluded baggage />
        /// </summary>
        public string ExcludedBaggage { get; set; }

        public const string CorrelationIdTag = "correlationId";
        public const string CorrelationIdBaggage = "x-correlation-id";

        private HashSet<string> _excludedBaggage = new(new[]
        {
            CorrelationIdBaggage
        });


        /// <summary>
        /// A https://messagetemplates.org template, as text. For example, the string <c>Hello {Name}!</c>.
        /// </summary>
        /// <remarks>See also https://opentelemetry.io/docs/reference/specification/logs/semantic_conventions/ and
        /// <see cref="SemanticConventions"/>.</remarks>
        public const string AttributeMessageTemplateText = "message_template.text";

        /// <summary>
        /// A https://messagetemplates.org template, hashed using MD5 and encoded as a 128-bit hexadecimal value.
        /// </summary>
        /// <remarks>See also https://opentelemetry.io/docs/reference/specification/logs/semantic_conventions/ and
        /// <see cref="SemanticConventions"/>.</remarks>
        public const string AttributeMessageTemplateMD5Hash = "message_template.hash.md5";

        private readonly List<KeyValue> _defaultAttributes = new();

        //private OtelPropertyConvertor _otelPropertyConvertor = new OtelPropertyConvertor();

        public OtelTraceContextLayout OtelTraceContextLayout { get; set; } = new OtelTraceContextLayout();

        public OtelBaggageLayout OtelBaggageLayout { get; set; }

        public OtelTagsLayout OtelTagsLayout { get; set; } 


        public OtelTarget()
        {
            CreateSerilogPropertyValueConverter();
        }

        private void CreateSerilogPropertyValueConverter()
        {
        }

        protected override void InitializeTarget()
        {
            OtelBaggageLayout = new OtelBaggageLayout();
            if(IncludeTags)
                OtelTagsLayout = new OtelTagsLayout();

            base.InitializeTarget();

            try
            {
                Layout = Layout.FromString("${message:withexception=false}");

                if (!string.IsNullOrEmpty(ExcludedTags))
                    _excludedTags = new HashSet<string>(ExcludedTags.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries));

                if (!string.IsNullOrEmpty(ExcludedBaggage))
                    _excludedBaggage = new HashSet<string>(ExcludedBaggage.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries));

                var eventInfo = LogEventInfo.CreateNullEvent();

                var otlpProtocol = GetProperty(eventInfo, OtlpProtocol, DefaultEnvOtelExporterOtlpProtocol, "grpc");
                var otlpEndpoint = GetProperty(eventInfo, OtlpEndpoint, DefaultEnvOtelExporterOtlpEndpoint,
                    "http://localhost:4317");

//#if !NET5_0_OR_GREATER                
//                otlpProtocol = "http";
//                var uri = new UriBuilder(otlpEndpoint);
//                if (uri.Port == 4317)
//                {
//                    InternalLogger.Info("Forcing OTEL Protocol from GRPC(4317) to HTTP(4318)");
//                    uri.Port = 4318;
//                    otlpEndpoint = uri.ToString();
//                    System.Environment.SetEnvironmentVariable(DefaultEnvOtelLogExporterOtlpEndpoint, otlpEndpoint);
//                }
//#endif
                if (!otlpEndpoint.EndsWith("/")) otlpEndpoint += "/";

                var otlpHeaders = GetHeaders(eventInfo);

                grpcExporter = otlpProtocol switch
                {
//#if NET5_0_OR_GREATER
                    "grpc" => new GrpcExporter($"{otlpEndpoint}v1/logs", otlpHeaders),
//#endif
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

        private string GetProperty(LogEventInfo eventInfo, Layout layout, string environmentVariable,
            string defaultValue)
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
                var otlpHeadersString = OtlpHeaders?.Render(eventInfo);
                if(string.IsNullOrEmpty(otlpHeadersString))
                    otlpHeadersString = System.Environment.GetEnvironmentVariable(DefaultEnvOtelExporterOtlpHeaders);

                if (!string.IsNullOrEmpty(otlpHeadersString))
                {
                    var keyValuePairs = otlpHeadersString!.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var keyValue in keyValuePairs)
                    {
                        var pair = keyValue.Split(new[] {'='}, StringSplitOptions.RemoveEmptyEntries);
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
                var fullSemVer = GitVersionInformation.FullSemVer;
                var resources = new Dictionary<string, object>
                {
                    ["telemetry.sdk.name"] = "nlog.targets.otel",
                    ["telemetry.sdk.language"] = "csharp",
                    ["telemetry.sdk.version"] = fullSemVer,
                    ["ddsource"] = "csharp"
                };

                _requestTemplate = RequestTemplateFactory.CreateRequestTemplate(resources);
                InitializeResourceAttributes(_requestTemplate, eventInfo);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error creating OTEL request template");
                throw;
            }
        }

        private void InitializeResourceAttributes(ExportLogsServiceRequest request, LogEventInfo eventInfo)
        {
            try
            {
                var resources = request.ResourceLogs[0].Resource.Attributes;

                ProcessCoreResourceAttributes(eventInfo, resources, "service.name", ServiceName, DefaultEnvOtelServiceName);
                ProcessCoreResourceAttributes(eventInfo, resources, "service.version", ServiceVersion,
                    DefaultEnvOtelServiceVersion);
                ProcessCoreResourceAttributes(eventInfo, resources, "deployment.environment", Environment,
                    DefaultEnvDeploymentEnvName);

                foreach (var attribute in ResourceAttributes)
                    ProcessCoreResourceAttributes(eventInfo, resources, attribute.Name, attribute.Layout);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error initializing resource attributes");
                throw;
            }
        }

        private void ProcessCoreResourceAttributes(LogEventInfo eventInfo, RepeatedField<KeyValue> resourceAttributes,
            string name, Layout? layout, string? environmentVar = null)
        {
            string? resourceValue = null;
            if (environmentVar != null)
                resourceValue = System.Environment.GetEnvironmentVariable(environmentVar);

            if (string.IsNullOrEmpty(resourceValue))
                resourceValue = layout?.Render(eventInfo);

            if (!string.IsNullOrEmpty(resourceValue))
                resourceAttributes.Add(PrimitiveConversions.NewStringAttribute(name, resourceValue!));
            else if (layout != null) _resourceAttributes[name] = layout;
        }

        private void CheckResourceAttributes()
        {
            try
            {
                if (!_resourceAttributes.Any()) return;

                var eventInfo = LogEventInfo.CreateNullEvent();

                foreach (var key in _resourceAttributes.Keys.ToList())
                {
                    if (!_resourceAttributes.TryGetValue(key, out var layout)) continue;

                    var resourceValue = layout?.Render(eventInfo);
                    if (!string.IsNullOrEmpty(resourceValue))
                    {
                        _resourceAttributes.TryRemove(key, out _);
                        var resourceAttribute = PrimitiveConversions.NewStringAttribute(key, resourceValue!);
                        _requestTemplate.ResourceLogs[0].Resource.Attributes.Add(resourceAttribute);
                    }
                }
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing resource attributes");
            }
        }

        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            try
            {
                CheckResourceAttributes();
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
                    Body = new AnyValue {StringValue = logMessage},
                    TimeUnixNano = PrimitiveConversions.ToUnixNano(logEvent.TimeStamp),
                    SeverityText = logEvent.Level.ToString(),
                    SeverityNumber = PrimitiveConversions.ToSeverityNumber(logEvent.Level),
                    Attributes = { }
                };

                ProcessTraceContext(logRecord, logEvent);
                ProcessBaggageAndTags(logRecord, logEvent);

                ProcessProperties(logRecord, logEvent);
                ProcessDefaultAttributes(logRecord, logEvent);
                ProcessMessageTemplate(logRecord, logEvent);
                ProcessException(logRecord, logEvent);

                request.ResourceLogs[0].ScopeLogs[0].LogRecords.Add(logRecord);
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error writing logEvent to OTEL");
            }
        }

        private void ProcessTraceContext(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                var traceContext = OtelTraceContextLayout.RetrieveTraceContext(logEvent);
                if (traceContext == null) return;

                if (traceContext.TraceId != null) 
                    logRecord.TraceId = PrimitiveConversions.ToOpenTelemetryTraceId(traceContext.TraceId);

                if (traceContext.SpanId != null) 
                    logRecord.SpanId = PrimitiveConversions.ToOpenTelemetrySpanId(traceContext.SpanId);

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

        private KeyValue AddAttributeFromString(string name, string value)
        {
            var processNameAttribute = PrimitiveConversions.NewAttribute(name,
                PrimitiveConversions.ToOpenTelemetryString(value));
            return processNameAttribute;
        }

        private void ProcessDefaultAttributes(LogRecord logRecord, LogEventInfo logEvent)
        {
            if (logEvent.LoggerName != null)
                logRecord.Attributes.Add(AddAttributeFromString("logger", logEvent.LoggerName));

            logRecord.Attributes.Add(AddAttributeFromString("ddsource","csharp"));

            logRecord.Attributes.Add(_defaultAttributes);

            if (IncludeCallSite)
            {
                logRecord.Attributes.Add(AddAttributeFromString("code.class", logEvent.CallerClassName));
                logRecord.Attributes.Add(AddAttributeFromString("code.method", logEvent.CallerMemberName));
            }

            if (IncludeThreadInfo)
            {
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "thread.name", "${threadname}"));
                logRecord.Attributes.Add(AddAttributeFromLayout(logEvent, "thread.id", "${threadid}"));
            }
        }

        private void ProcessBaggageAndTags(LogRecord logRecord, LogEventInfo logEvent)
        {
            try
            {
                var activityBaggage = OtelBaggageLayout.RetrieveBaggage(logEvent);

                if (IncludeCorrelationId)
                {
                    if (activityBaggage?.TryGetValue(CorrelationIdBaggage, out var correlationId) == true && !string.IsNullOrEmpty(correlationId))
                    {
                        logRecord.Attributes.Add(
                            PrimitiveConversions.NewStringAttribute(CorrelationIdTag,
                                correlationId));
                    }
                }

                if (IncludeBaggage)
                {
                    var filteredBaggage = activityBaggage
                        ?.Where(baggageItem => baggageItem.Value != null && !_excludedBaggage.Contains(baggageItem.Key));

                    if (filteredBaggage == null) return;

                    foreach (var baggageItem in filteredBaggage)
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
                    var tags = OtelTagsLayout.RetrieveTags(logEvent)
                        ?.Where(item => item.Value != null && !_excludedTags.Contains(item.Key));

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
                attrs.Add(PrimitiveConversions.NewStringAttribute(SemanticConventions.AttributeExceptionType,
                    ex.GetType().ToString()));

                if (ex.Message != "")
                    attrs.Add(PrimitiveConversions.NewStringAttribute(SemanticConventions.AttributeExceptionMessage,
                        ex.Message));

                if (ex.ToString() != "")
                    attrs.Add(PrimitiveConversions.NewStringAttribute(SemanticConventions.AttributeExceptionStacktrace,
                        ex.ToString()));
            }
            catch (Exception e)
            {
                InternalLogger.Error(e.FlattenToActualException(), "Error processing Exception");
                throw;
            }
        }
    }
}
    
