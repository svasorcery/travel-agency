using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Application;

public sealed class RailReadAdmission
{
    private readonly SemaphoreSlim permits = new(2, 2);

    public bool TryEnter() => permits.Wait(0);

    public void Release() => permits.Release();
}

public static class RailSearchCacheKey
{
    public static string Create(RailRequest request, RailProfile profile, string generation) =>
        string.Join(
            "|",
            "rail-m1-v1",
            profile.Epoch,
            generation,
            request.From.Source,
            request.From.Code,
            request.To.Code,
            request.DepartureDate,
            request.Transport
        );
}
