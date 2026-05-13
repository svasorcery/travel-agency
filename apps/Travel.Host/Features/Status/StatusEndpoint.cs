using Microsoft.AspNetCore.Authorization;
using Travel.Host.Persistence;
using Wolverine.Http;

namespace Travel.Host.Features.Status;

public static class StatusEndpoint
{
    [AllowAnonymous]
    [WolverineGet("/api/status")]
    public static async Task<StatusResponse> GetAsync(
        HostDbContext db,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var version = await db.GetServerVersionAsync(ct);
        return new StatusResponse(version, "ok", clock.GetUtcNow());
    }
}
