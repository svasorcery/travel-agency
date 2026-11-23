using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Travel.AI.Observability;
using Travel.IntegrationContracts.AI.NlSearch;
using Wolverine.Attributes;

namespace Travel.AI.NlSearch;

public static class FlightSearchPreviewAiHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private static readonly NlSearchOperation<FlightSearchPreviewFragments> Operation = new(
        "flights.nl_search_preview",
        FlightSearchPreviewMessageIdentity.Requested,
        "fictional",
        new(
            () => NlSearchPrompts.NlSearchPreviewSystem,
            ChatResponseFormat.ForJsonSchema<PreviewOutput>(Json),
            Parse
        )
    );

    internal static bool IsEnabled(IConfiguration configuration, IHostEnvironment environment) =>
        environment.IsEnvironment("Testing")
        && configuration.GetValue<bool>("Flights:NlSearchPreview:Enabled");

    [WolverineHandler]
    public static async Task<FlightSearchPreviewParsed> Handle(
        FlightSearchPreviewRequested request,
        IServiceScopeFactory scopes,
        TimeProvider time,
        AiMetrics metrics,
        NlSearchAdmission admission,
        CancellationToken ct
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        if (
            !IsEnabled(
                services.GetRequiredService<IConfiguration>(),
                services.GetRequiredService<IHostEnvironment>()
            )
            || services.GetKeyedService<IChatClient>(
                FlightSearchPreviewMessageIdentity.ChatClientKey
            )
                is not { } chat
        )
            return new(request.CorrelationId, null);

        try
        {
            var extraction = await NlSearchAiHandler.RunAsync(
                request.Query,
                request.ReferenceDate,
                request.CorrelationId,
                Operation,
                chat,
                scopes,
                time,
                metrics,
                log: null,
                admission,
                ct
            );
            return new(request.CorrelationId, extraction.Result);
        }
        catch (NlSearchExtractionException)
        {
            return new(request.CorrelationId, null);
        }
    }

    private static FlightSearchPreviewFragments? Parse(string text)
    {
        var output = JsonSerializer.Deserialize<PreviewOutput>(text, Json);
        if (output?.Restrictions is null || output.Restrictions.Any(fragment => fragment is null))
            return null;
        return new(
            output.Origin,
            output.Destination,
            output.Departure,
            output.Return,
            output.Party,
            output.TripType,
            output.Restrictions
        );
    }

    private sealed record PreviewOutput(
        [property: JsonRequired] string? Origin,
        [property: JsonRequired] string? Destination,
        [property: JsonRequired] string? Departure,
        [property: JsonRequired] string? Return,
        [property: JsonRequired] string? Party,
        [property: JsonRequired] string? TripType,
        [property: JsonRequired] string[] Restrictions
    );
}
