using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Application;

namespace Travel.Modules.Hotels.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class HotelHttpTests(HotelStoreFixture fixture) : IClassFixture<HotelStoreFixture>
{
    [Fact]
    public async Task Anonymous_search_then_owned_quote_private_reads_and_null_party_guards_use_real_routes()
    {
        var ct = TestContext.Current.CancellationToken;
        var dates = DuffelStaysProviderTests.Request().Dates;
        using var searchResponse = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn = dates.CheckIn,
                checkOut = dates.CheckOut,
                adults = 1,
            },
            ct
        );
        searchResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        searchResponse.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync(ct));
        using var ratesResponse = await fixture.Client.PostAsJsonAsync(
            "/api/hotels/rates",
            new
            {
                searchId = search.RootElement.GetProperty("id").GetGuid(),
                selectionKey = "fictional_rate",
            },
            ct
        );
        ratesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var rates = JsonDocument.Parse(await ratesResponse.Content.ReadAsStringAsync(ct));
        var id = Guid.NewGuid();
        using var quote = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/hotels/reservations/{id}/quote"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    ratesId = rates.RootElement.GetProperty("id").GetGuid(),
                    selectionKey = "fictional_rate",
                    expectedRevision = (int?)null,
                }
            ),
        };
        quote.Headers.Add("X-Fictional-Owner", Guid.NewGuid().ToString());
        using var quoteResponse = await fixture.Client.SendAsync(quote, ct);
        quoteResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (
            var party in new[]
            {
                "null",
                "{\"guests\":[null],\"email\":\"fictional@example.invalid\",\"phone\":\"+44000000000\",\"adultsConfirmed\":true,\"checkInConfirmed\":true}",
                "{\"guests\":[{\"givenName\":\"Fictional\",\"familyName\":\"Traveller\"}],\"email\":\"fictional@example.invalid\",\"phone\":\"+++++\",\"adultsConfirmed\":true,\"checkInConfirmed\":true}",
            }
        )
        {
            using var purchase = new HttpRequestMessage(
                HttpMethod.Post,
                $"/api/hotels/reservations/{id}/purchase"
            )
            {
                Content = new StringContent(
                    $"{{\"actionId\":\"{Guid.NewGuid()}\",\"quoteRevision\":1,\"consent\":true,\"party\":{party}}}",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            purchase.Headers.Add(
                "X-Fictional-Owner",
                quote.Headers.GetValues("X-Fictional-Owner").Single()
            );
            using var result = await fixture.Client.SendAsync(purchase, ct);
            result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        using (var check = fixture.Host.Services.CreateScope())
        {
            var stored = await check
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .LoadAsync(
                    id,
                    Guid.Parse(quote.Headers.GetValues("X-Fictional-Owner").Single()),
                    ct
                );
            stored.Value!.AttemptId.ShouldBeNull();
            fixture.Provider.Creates.ShouldBe(0);
        }
        using var foreign = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/hotels/reservations/{id}"
        );
        foreign.Headers.Add("X-Fictional-Owner", Guid.NewGuid().ToString());
        using var hidden = await fixture.Client.SendAsync(foreign, ct);
        hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var anonymous = await fixture.Client.GetAsync($"/api/hotels/reservations/{id}", ct);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var decrypts = fixture.Protection.Decrypts;
        var creates = fixture.Provider.Creates;
        foreach (
            var owner in new[]
            {
                quote.Headers.GetValues("X-Fictional-Owner").Single(),
                "invalid-subject",
            }
        )
        {
            using var forbidden = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/hotels/reservations/{id}"
            );
            forbidden.Headers.Add("X-Fictional-Owner", owner);
            if (owner != "invalid-subject")
                forbidden.Headers.Add("X-Fictional-Scope", "none");
            using var denied = await fixture.Client.SendAsync(forbidden, ct);
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            denied.Headers.CacheControl!.NoStore.ShouldBeTrue();
        }
        fixture.Protection.Decrypts.ShouldBe(decrypts);
        fixture.Provider.Creates.ShouldBe(creates);
    }
}
