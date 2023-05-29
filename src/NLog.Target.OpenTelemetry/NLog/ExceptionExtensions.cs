namespace NLog.OpenTelemetry;

#pragma warning disable CS8618, CS1591
internal static class ExceptionExtensions
{
    public static Exception FlattenToActualException(this Exception exception)
    {
        if (!(exception is AggregateException aggregateException))
            return exception;

        var flattenException = aggregateException.Flatten();
        if (flattenException.InnerExceptions.Count == 1)
        {
            return flattenException.InnerExceptions[0];
        }

        return flattenException;
    }
}