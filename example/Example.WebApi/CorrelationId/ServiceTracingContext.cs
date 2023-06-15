#pragma warning disable CS8600
#pragma warning disable CS8603

namespace Jedi.ServiceFabric.Tracing
{
    public static class ServiceTracingContext
    {
        const string CorrelationKey = "CorrelationId";
        const string ServiceDetailsKey = "ServiceDetails";

        public static void CreateRequestCorrelationId(bool preserveExisting = true)
        {
            if (preserveExisting && HasCorrelationId()) return;

            CallContext.SetData(CorrelationKey, GenerateId());
        }

        public static bool HasCorrelationId()
        {
            return !string.IsNullOrEmpty(GetRequestCorrelationId());
        }

        public static string GetRequestCorrelationId()
        {
            return CallContext.GetData(CorrelationKey) as string;
        }

        public static void SetRequestCorrelationId(string value)
        {
            CallContext.SetData(CorrelationKey, value);
        }


        public static string GetRequestServiceDetails()
        {
            return CallContext.GetData(ServiceDetailsKey) as string;
        }

        public static void SetRequestServiceDetails(string value)
        {
            CallContext.SetData(ServiceDetailsKey, value);
        }

        private static string GenerateId()
        {
            return Guid.NewGuid().ToString();
        }
    }
}
