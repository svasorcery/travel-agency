using ErrorOr;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Host.Tests.Integration.NlSearch;

public sealed partial class NlSearchTransportFixture
{
    private const string PreviewFlag = "Flights:NlSearchPreview:Enabled";
    private readonly bool _previewEnabled;
    private readonly PreviewChatClient _previewChat = new();
    private readonly PreviewRecordingSearch _previewSearch = new();
    private readonly BriefFixtureLogs _previewLogs = new();

    internal int PreviewModelCalls => _previewChat.CallCount;
    internal int LegacyModelCalls => _replicaOneChat.CallCount + _replicaTwoChat.CallCount;
    internal int PreviewTestSearches => _previewSearch.Searches;
    internal int PreviewTestSupplierCalls => _previewSearch.SupplierCalls;
    internal SearchCriteria? LastPreviewTestCriteria => _previewSearch.LastCriteria;
    internal string[] PreviewLogs => _previewLogs.Messages.ToArray();

    internal HttpClient CreatePreviewHttpClient() => _host!.Server.CreateClient();

    internal void DisablePreviewOnHost() =>
        _host!.Services.GetRequiredService<IConfiguration>()[PreviewFlag] = "false";

    private void ConfigurePreviewHost(IWebHostBuilder builder)
    {
        builder.UseSetting(PreviewFlag, "true");
        builder.ConfigureLogging(logging => logging.AddProvider(_previewLogs));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISearchCache>();
            services.RemoveAll<IFlightSearchProvider>();
            services.AddSingleton<ISearchCache>(_previewSearch);
            services.AddSingleton<IFlightSearchProvider>(_previewSearch);
        });
    }

    private void ConfigurePreviewAi(IWebHostBuilder builder)
    {
        builder.UseSetting(PreviewFlag, "true");
        builder.ConfigureLogging(logging => logging.AddProvider(_previewLogs));
        builder.ConfigureServices(services =>
            services.AddKeyedSingleton<IChatClient>(
                FlightSearchPreviewMessageIdentity.ChatClientKey,
                _previewChat
            )
        );
    }

    // A cache lookup witnesses one invocation of the real SearchFlightsHandler. The cache always misses.
    private sealed class PreviewRecordingSearch : ISearchCache, IFlightSearchProvider
    {
        public ProviderId Id => ProviderId.Duffel;
        public int Searches { get; private set; }
        public int SupplierCalls { get; private set; }
        public SearchCriteria? LastCriteria { get; private set; }

        public FlightSearchSupport GetSupport(SearchCriteria criteria) =>
            FlightSearchSupport.Available;

        public Task<SearchResult?> TryGetAsync(string key, CancellationToken ct)
        {
            Searches++;
            return Task.FromResult<SearchResult?>(null);
        }

        public Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct) =>
            throw new InvalidOperationException("An empty result is not cached.");

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        )
        {
            SupplierCalls++;
            LastCriteria = criteria;
            return Task.FromResult<ErrorOr<IReadOnlyList<Offer>>>(Array.Empty<Offer>());
        }
    }

    private sealed class PreviewChatClient : IChatClient
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _callCount);
            var input = messages.Single(m => m.Role == ChatRole.User).Text;
            var text =
                input.Contains("Из Москвы", StringComparison.Ordinal)
                    ? """
                        {"origin":"Из Москвы","destination":"в Сочи","departure":"12 декабря 2026","return":"обратно 15 декабря 2026","party":"двое взрослых","tripType":null,"restrictions":[]}
                        """
                : input.Contains("на выходные", StringComparison.Ordinal)
                    ? """
                        {"origin":null,"destination":"в Сочи","departure":"на выходные","return":null,"party":"нас двое","tripType":null,"restrictions":[]}
                        """
                : "invalid fictional reply";
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
                {
                    ModelId = "fictional-preview",
                    Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 8 },
                }
            );
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
