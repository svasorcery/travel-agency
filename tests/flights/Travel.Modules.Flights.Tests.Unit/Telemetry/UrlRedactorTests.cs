using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Telemetry;

public sealed class UrlRedactorTests
{
    [Fact]
    public void Redactor_strips_token_query_param()
    {
        const string url =
            "https://api.travelpayouts.com/aviasales/v3/prices_for_dates?token=SECRET&origin=LED&destination=DME";

        var redacted = UrlRedactor.Redact(url);

        redacted.ShouldContain("token=REDACTED");
        redacted.ShouldNotContain("SECRET");
        redacted.ShouldContain("origin=LED");
        redacted.ShouldContain("destination=DME");
    }

    [Fact]
    public void Redactor_handles_url_without_token()
    {
        const string url = "https://api.frankfurter.app/latest?base=USD&symbols=EUR";

        var redacted = UrlRedactor.Redact(url);

        redacted.ShouldBe(url);
    }

    [Fact]
    public void Redactor_handles_token_at_end_of_query()
    {
        const string url = "https://api.travelpayouts.com/v3/prices?origin=LED&token=MYSECRET";

        var redacted = UrlRedactor.Redact(url);

        redacted.ShouldContain("token=REDACTED");
        redacted.ShouldNotContain("MYSECRET");
    }
}
