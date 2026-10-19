using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelbedsRequestContractTests
{
    [Fact]
    public async Task External_profile_uses_documented_string_geolocation_and_signature_from_injected_epoch()
    {
        // Ordinary HttpClient deliberately exercises the External serialization path;
        // its terminal handler is owned, so there are no sockets or certificate lookups.
        var time = new FakeTimeProvider(
            new DateTimeOffset(2026, 10, 19, 12, 34, 56, TimeSpan.Zero)
        );
        var options = Options.Create(
            new HotelbedsOptions
            {
                Enabled = true,
                ExternalUseApproved = true,
                AccountAlias = "owned-external-alias",
                ConfigurationRevision = "owned-1",
                ApprovalRevision = "owned-1",
                ApiKey = "owned-not-a-real-key",
                Secret = "owned-not-a-real-secret",
                ClientCertificateThumbprint = new string('A', 40),
            }
        );
        var module = Options.Create(
            new HotelsOptions
            {
                Aggregation = new()
                {
                    Enabled = true,
                    ExternalUseApproved = true,
                    ApprovedUseScope = "DuffelHotelbedsSearchComparisonBooking",
                    ConfigurationRevision = "owned-1",
                    ApprovalRevision = "owned-1",
                },
            }
        );
        var capture = new Capture();
        using var http = new HttpClient(capture);
        var provider = new HotelbedsStayProvider(http, options, new(module, time), time, module);
        var request = new StaySearch(
            new("owned-area", 51.5074m, -0.1278m, 7),
            new(new(2026, 11, 2), new(2026, 11, 4)),
            new(2)
        );
        await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(1));
        await provider.SearchAsync(request, TestContext.Current.CancellationToken);
        capture.Requests.Count.ShouldBe(2);
        foreach (var (body, key, signature, uri, method) in capture.Requests)
        {
            key.ShouldBe(options.Value.ApiKey);
            uri.ShouldBe("https://api-mtls.test.hotelbeds.com/hotel-api/1.0/hotels");
            method.ShouldBe(HttpMethod.Post);
            using var json = JsonDocument.Parse(body);
            var geo = json.RootElement.GetProperty("geolocation");
            geo.GetProperty("latitude").ValueKind.ShouldBe(JsonValueKind.String);
            geo.GetProperty("latitude").GetString().ShouldBe("51.5074");
            geo.GetProperty("longitude").GetString().ShouldBe("-0.1278");
            geo.GetProperty("radius").ValueKind.ShouldBe(JsonValueKind.String);
            geo.GetProperty("radius").GetString().ShouldBe("7");
            geo.GetProperty("unit").GetString().ShouldBe("km");
            json.RootElement.GetProperty("occupancies")[0]
                .GetProperty("adults")
                .GetInt32()
                .ShouldBe(2);
            signature.Length.ShouldBe(64);
        }
        var initialEpoch = new DateTimeOffset(
            2026,
            10,
            19,
            12,
            34,
            56,
            TimeSpan.Zero
        ).ToUnixTimeSeconds();
        for (var i = 0; i < 2; i++)
        {
            var expected = Convert
                .ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            options.Value.ApiKey
                                + options.Value.Secret
                                + (initialEpoch + i).ToString(
                                    System.Globalization.CultureInfo.InvariantCulture
                                )
                        )
                    )
                )
                .ToLowerInvariant();
            capture.Requests[i].Signature.ShouldBe(expected);
        }
        capture.Requests[0].Signature.ShouldNotBe(capture.Requests[1].Signature);
    }

    private sealed class Capture : HttpMessageHandler
    {
        public List<(
            string Body,
            string Key,
            string Signature,
            string Uri,
            HttpMethod Method
        )> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Requests.Add(
                (
                    await request.Content!.ReadAsStringAsync(ct),
                    request.Headers.GetValues("Api-key").Single(),
                    request.Headers.GetValues("X-Signature").Single(),
                    request.RequestUri!.AbsoluteUri,
                    request.Method
                )
            );
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"hotels\":{\"checkIn\":\"2026-11-02\",\"checkOut\":\"2026-11-04\",\"hotels\":[]}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }
}
