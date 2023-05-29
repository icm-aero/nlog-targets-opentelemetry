using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace Serilog.Stubs;

/// <summary>
/// 
/// </summary>
public class LoggerAuditSinkConfiguration
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="sink"></param>
    /// <param name="optionsRestrictedToMinimumLevel"></param>
    /// <param name="optionsLevelSwitch"></param>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    internal LoggerConfiguration Sink(ActivityContextCollectorSink sink, LogEventLevel optionsRestrictedToMinimumLevel, LoggingLevelSwitch? optionsLevelSwitch)
    {
        throw new NotImplementedException();
    }
}