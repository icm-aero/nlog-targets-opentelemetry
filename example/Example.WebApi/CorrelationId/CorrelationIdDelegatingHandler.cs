using Jedi.ServiceFabric.Tracing;
using Microsoft.Extensions.Options;

namespace Jedi.ServiceFabric.AspNetCore.Correlation
{
    public class CorrelationIdDelegatingHandler : DelegatingHandler
    {
        private readonly IOptions<CorrelationIdOptions> options;

        public CorrelationIdDelegatingHandler(
            IOptions<CorrelationIdOptions> options)
        {
            this.options = options;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains(this.options.Value.Header))
            {
                var correlationIdString = ServiceTracingContext.GetRequestCorrelationId();
                request.Headers.Add(this.options.Value.Header, correlationIdString);
            }

            // Else the header has already been added due to a retry.

            return base.SendAsync(request, cancellationToken);
        }
    }
}