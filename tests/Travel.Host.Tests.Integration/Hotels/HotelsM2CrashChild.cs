using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;

namespace Travel.Host.Tests.Integration.Hotels;

public sealed record HotelsM2CrashManifest(
    string Connection,
    HotelPiiProtectionOptions Privacy,
    Guid ReservationId,
    HotelPurchase Purchase,
    string Marker,
    bool AfterEffect,
    string RateScopes
);

public static class HotelsM2CrashChild
{
    public static async Task RunAsync(string manifestPath, CancellationToken ct)
    {
        var manifest = JsonSerializer.Deserialize<HotelsM2CrashManifest>(
            await File.ReadAllTextAsync(manifestPath, ct)
        )!;
        await using var fixture = new HotelsHostFixture { Multisupplier = true };
        fixture.HotelbedsWire.ImportRateScopes(manifest.RateScopes);
        if (manifest.AfterEffect)
            fixture.HotelbedsWire.AfterEffect = () =>
                StopAtBoundary(
                    manifest.Marker,
                    "supplier-effect",
                    fixture.HotelbedsWire.Creates,
                    ct
                );
        else
            fixture.ConfigureTestServices = services =>
            {
                services.RemoveAll<IHotelReservationStore>();
                services.AddScoped<IHotelReservationStore>(s => new StopAfterClaim(
                    s.GetRequiredService<HotelReservationStore>(),
                    manifest.Marker,
                    ct
                ));
            };
        await fixture.StartAttachedAsync(manifest.Connection, manifest.Privacy);
        using var scope = fixture.Services.CreateScope();
        var row = (
            await scope
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .LoadAsync(manifest.ReservationId, null, ct)
        ).Value!;
        var result = await scope
            .ServiceProvider.GetRequiredService<HotelReservationService>()
            .PurchaseAsync(row.OwnerId, row.Id, manifest.Purchase, ct);
        throw new InvalidOperationException(
            "Crash boundary was not reached: "
                + (result.IsError ? result.FirstError.Code : "unexpected completion")
        );
    }

    private static async Task StopAtBoundary(
        string marker,
        string boundary,
        int creates,
        CancellationToken ct
    )
    {
        var ownedDirectory = Path.GetFullPath(Path.GetDirectoryName(marker)!);
        if (
            !ownedDirectory.StartsWith(
                Path.GetFullPath(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase
            )
            || !Path.GetFileName(ownedDirectory)
                .StartsWith("travel-hotels-host-", StringComparison.Ordinal)
        )
            throw new InvalidOperationException(
                "Crash marker is outside the parent-owned fixture directory."
            );
        var ownedPrefix = ownedDirectory + Path.DirectorySeparatorChar;
        if (
            !Path.GetFullPath(marker).StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFullPath(marker + ".writing")
                .StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException(
                "Crash marker rename is outside the parent-owned directory."
            );
        await File.WriteAllTextAsync(
            marker + ".writing",
            JsonSerializer.Serialize(
                new
                {
                    pid = Environment.ProcessId,
                    boundary,
                    source = "hotelbeds",
                    creates,
                    fictional = true,
                }
            ),
            ct
        );
        File.Move(marker + ".writing", marker);
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private sealed class StopAfterClaim(
        HotelReservationStore inner,
        string marker,
        CancellationToken stop
    ) : IHotelReservationStore
    {
        public Task<ErrorOr<HotelReservation?>> LoadAsync(
            Guid id,
            Guid? owner,
            CancellationToken ct
        ) => inner.LoadAsync(id, owner, ct);

        public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
            inner.ListAsync(owner, ct);

        public async Task<ErrorOr<bool>> SaveAsync(
            HotelReservation row,
            int expected,
            HotelWork[] work,
            CancellationToken ct
        )
        {
            var result = await inner.SaveAsync(row, expected, work, ct);
            if (
                !result.IsError
                && result.Value
                && row.AttemptId is not null
                && work.Any(w => w.Escalation)
            )
                await StopAtBoundary(marker, "committed-claim-before-send", 0, stop);
            return result;
        }
    }
}
