using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.Http;
using Travel.Shared.Infrastructure.Telemetry;

namespace Travel.ServiceDefaults.Telemetry;

internal sealed class HttpUrlRedactionOptionsConfigurator(
    IEnumerable<IHttpUrlRedactionContributor> contributors
) : IConfigureOptions<HttpClientTraceInstrumentationOptions>
{
    private readonly HttpUrlRedactor _redactor = new(contributors);

    public void Configure(HttpClientTraceInstrumentationOptions options)
    {
        var previousEnricher = options.EnrichWithHttpRequestMessage;
        options.EnrichWithHttpRequestMessage = (activity, request) =>
        {
            previousEnricher?.Invoke(activity, request);
            if (request.RequestUri is not null)
                activity.SetTag("url.full", _redactor.Redact(request.RequestUri.AbsoluteUri));
        };
    }
}
