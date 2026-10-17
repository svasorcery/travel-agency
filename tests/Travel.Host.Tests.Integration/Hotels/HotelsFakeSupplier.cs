using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Travel.Host.Tests.Integration.Hotels;

public sealed record HotelsFakeRetentionSnapshot(
    int RateEntries,
    int QuoteEntries,
    int BookingEntries,
    int PreparationEntries,
    long? RateValueJsonUtf8Bytes,
    long? QuoteValueJsonUtf8Bytes,
    long? BookingValueJsonUtf8Bytes,
    long? PreparationMapJsonUtf8Bytes
);

public sealed class HotelsFakeSupplier : HttpMessageHandler
{
    // Diagnostic reads only: value JSON bytes are not managed-object/dictionary memory.
    // Detailed serialization is permitted outside the timed diagnostic window only.
    public HotelsFakeRetentionSnapshot GetRetentionSnapshot(bool includePayloadBytes) =>
        new(
            rates.Count,
            quotes.Count,
            bookings.Count,
            preparationCounts.Count,
            includePayloadBytes ? JsonBytes(rates.Values) : null,
            includePayloadBytes ? JsonBytes(quotes.Values) : null,
            includePayloadBytes ? JsonBytes(bookings.Values) : null,
            includePayloadBytes
                ? Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(preparationCounts))
                : null
        );

    private static long JsonBytes(IEnumerable<JsonNode> values) =>
        values.Sum(value => (long)Encoding.UTF8.GetByteCount(value.ToJsonString()));

    private readonly ConcurrentDictionary<string, JsonNode> rates = new();
    private readonly ConcurrentDictionary<string, JsonNode> quotes = new();
    private readonly ConcurrentDictionary<string, JsonNode> bookings = new();
    private readonly ConcurrentDictionary<string, int> preparationCounts = new();
    private int active,
        peak,
        sequence,
        creates,
        reads;
    public int DelayMilliseconds { get; set; } = 100;
    public bool Degraded { get; set; }
    public bool InconclusiveReads { get; set; }
    public string NextCreate { get; set; } = "confirmed";
    public int TermsRevision { get; set; }
    public bool ChangeTermsOnRefresh { get; set; }
    public int Active => active;
    public int Peak => peak;
    public int Creates => creates;
    public int Reads => reads;
    public int LastGuestCount { get; private set; }
    public string? LastBookingId { get; private set; }
    public string? LastQuoteId { get; private set; }
    public TaskCompletionSource CreateStarted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? CreateGate { get; set; }
    public ConcurrentQueue<(DateTimeOffset At, string Operation)> Starts { get; } = new();
    public Action<string>? OnStart { get; set; }
    public ConcurrentQueue<(
        DateTimeOffset At,
        string BookingId,
        int Status,
        double Milliseconds
    )> ReadCompletions { get; } = new();

    private static JsonNode Template(string name) =>
        JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "HotelsFixtures", name + ".json")
            )
        )!;

    private static JsonNode Copy(JsonNode node) => node.DeepClone();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var began = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var status = 0;
        try
        {
            var response = await RespondAsync(request, ct);
            status = (int)response.StatusCode;
            return response;
        }
        finally
        {
            if (
                request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.StartsWith(
                    "/stays/bookings/",
                    StringComparison.Ordinal
                )
            )
                ReadCompletions.Enqueue(
                    (
                        began,
                        request.RequestUri.AbsolutePath.Split('/')[3],
                        status,
                        timer.Elapsed.TotalMilliseconds
                    )
                );
        }
    }

    private async Task<HttpResponseMessage> RespondAsync(
        HttpRequestMessage request,
        CancellationToken ct
    )
    {
        var path = request.RequestUri!.AbsolutePath;
        var operation =
            path == "/stays/search" ? "search"
            : path.Contains("fetch_all_rates", StringComparison.Ordinal) ? "rates"
            : path == "/stays/quotes" ? "prepare"
            : request.Method == HttpMethod.Get ? "read"
            : "create";
        var ordinal = Interlocked.Increment(ref sequence);
        Starts.Enqueue((DateTimeOffset.UtcNow, operation));
        OnStart?.Invoke(operation);
        var current = Interlocked.Increment(ref active);
        int old;
        do
        {
            old = peak;
            if (old >= current)
                break;
        } while (Interlocked.CompareExchange(ref peak, current, old) != old);
        try
        {
            await Task.Delay(
                Degraded && ordinal % 10 < 3 && operation != "create" ? 3000 : DelayMilliseconds,
                ct
            );
            if (Degraded && ordinal % 10 == 9 && operation != "create")
                return Reply(
                    new JsonObject { ["error"] = "fictional safe failure" },
                    HttpStatusCode.BadGateway
                );
            JsonNode? body = request.Content is null
                ? null
                : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!["data"];
            if (operation == "search")
            {
                var result = Template("search");
                var data = result["data"]!["results"]![0]!;
                data["id"] = "srr_" + ordinal;
                data["check_in_date"] = Copy(body!["check_in_date"]!);
                data["check_out_date"] = Copy(body["check_out_date"]!);
                data["guests"] = Copy(body["guests"]!);
                data["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(5);
                data["accommodation"]!["rooms"]![0]!["rates"]![0]!["id"] = "rat_" + ordinal;
                data["accommodation"]!["rooms"]![0]!["rates"]![0]!["expires_at"] =
                    DateTimeOffset.UtcNow.AddMinutes(5);
                rates[data["id"]!.GetValue<string>()] = Copy(data);
                rates["rat_" + ordinal] = Copy(data);
                return Reply(result);
            }
            if (operation == "rates")
            {
                var data = Copy(rates[path.Split('/')[3]]);
                return Reply(new JsonObject { ["data"] = data });
            }
            if (operation == "prepare")
            {
                var rateId = body!["rate_id"]!.GetValue<string>();
                var source = rates[rateId];
                var preparedCount = preparationCounts.AddOrUpdate(
                    rateId,
                    1,
                    (_, count) => count + 1
                );
                var result = Template("quote");
                var data = result["data"]!;
                data["id"] = "quo_" + ordinal;
                data["check_in_date"] = Copy(source["check_in_date"]!);
                data["check_out_date"] = Copy(source["check_out_date"]!);
                data["guests"] = Copy(source["guests"]!);
                data["accommodation"] = Copy(source["accommodation"]!);
                var revision = Math.Max(
                    TermsRevision,
                    ChangeTermsOnRefresh ? preparedCount - 1 : 0
                );
                if (revision > 0)
                    data["accommodation"]!["rooms"]![0]!["rates"]![0]!["conditions"] =
                        new JsonArray(
                            new JsonObject
                            {
                                ["title"] = "Условия заселения",
                                ["description"] = "Выдуманное условие " + revision,
                            }
                        );
                quotes[data["id"]!.GetValue<string>()] = Copy(data);
                return Reply(result);
            }
            if (operation == "read")
            {
                Interlocked.Increment(ref reads);
                return InconclusiveReads
                    ? Reply(new JsonObject(), HttpStatusCode.BadGateway)
                    : Reply(new JsonObject { ["data"] = Copy(bookings[path.Split('/')[3]]) });
            }
            Interlocked.Increment(ref creates);
            var quote = quotes[body!["quote_id"]!.GetValue<string>()];
            LastQuoteId = body["quote_id"]!.GetValue<string>();
            LastGuestCount = body["guests"]!.AsArray().Count;
            var booking = Copy(Template("booking201")["data"]!);
            var id = "bok_" + ordinal;
            booking["id"] = id;
            LastBookingId = id;
            booking["check_in_date"] = Copy(quote["check_in_date"]!);
            booking["check_out_date"] = Copy(quote["check_out_date"]!);
            booking["guest_types"] = Copy(quote["guests"]!);
            booking["accommodation"] = Copy(quote["accommodation"]!);
            var outcome = NextCreate;
            NextCreate = "confirmed";
            if (outcome != "pending")
                bookings[id] = Copy(booking);
            CreateStarted.TrySetResult();
            if (CreateGate is not null)
                await CreateGate.Task.WaitAsync(ct);
            if (outcome == "lost")
                throw new HttpRequestException("Fictional reply lost after effect.");
            if (outcome == "reserved")
                return Reply(
                    new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["message"] =
                                "The booking has been confirmed. It will appear in the system soon.",
                        },
                    }
                );
            if (outcome == "pending")
                return Reply(
                    new JsonObject { ["data"] = new JsonObject { ["message"] = "Pending" } },
                    HttpStatusCode.Accepted
                );
            return Reply(
                new JsonObject
                {
                    ["data"] = booking,
                    ["meta"] = new JsonObject { ["request_id"] = "req_" + ordinal },
                },
                HttpStatusCode.Created
            );
        }
        finally
        {
            Interlocked.Decrement(ref active);
        }
    }

    private static HttpResponseMessage Reply(
        JsonNode body,
        HttpStatusCode code = HttpStatusCode.OK
    ) =>
        new(code)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };

    // Models the remote fictional supplier lifetime across replacement Host clients; no sockets/resources are held.
    protected override void Dispose(bool disposing) { }
}
