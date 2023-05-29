using OpenTelemetry.Proto.Common.V1;
using Serilog.Capturing;
using Serilog.Core;
using Serilog.Parsing;
using Serilog.Sinks.OpenTelemetry.ProtocolHelpers;

namespace NLog.OpenTelemetry;

#pragma warning disable CS8618, CS1591
internal class OtelPropertyConvertor
{
    private PropertyValueConverter _propertyValueConverter;
    public OtelPropertyConvertor()
    {
        const int maximumDestructuringDepth = 10;
        const int maximumStringLength = int.MaxValue;
        const int maximumCollectionCount = int.MaxValue;
        List<Type> additionalScalarTypes = new();
        List<IDestructuringPolicy> additionalDestructuringPolicies = new();
        const bool auditing = false;

        _propertyValueConverter = new PropertyValueConverter(
            maximumDestructuringDepth,
            maximumStringLength,
            maximumCollectionCount,
            additionalScalarTypes,
            additionalDestructuringPolicies,
            auditing);
    }

    public AnyValue ConvertObjectToAnyValue(object value)
    {
        var logEventProperty = _propertyValueConverter.CreatePropertyValue(value, Destructuring.Destructure);
        var v = PrimitiveConversions.ToOpenTelemetryAnyValue(logEventProperty);
        return v;
    }
}