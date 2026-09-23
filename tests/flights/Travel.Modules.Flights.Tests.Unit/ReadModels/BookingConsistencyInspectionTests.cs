using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.ReadModels;

public sealed class BookingConsistencyInspectionTests
{
    [Fact]
    public void Inspect_json_exposes_versions_bootstrap_and_derived_mismatch_without_payloads()
    {
        var id = Guid.NewGuid();
        var inspection = new BookingConsistencyInspection(
            new ProjectionValidation(
                id,
                5,
                -1,
                true,
                [new("ProjectionBootstrapRequired", true), new("DerivedFieldsMismatch", true)]
            ),
            [new(Guid.NewGuid(), "ReconcileOrderReadModel", "DeadLetter", false)]
        );

        var json = JsonSerializer.Serialize(inspection);
        json.ShouldContain("\"SourceVersion\":5");
        json.ShouldContain("\"PersistedVersion\":-1");
        json.ShouldContain("\"BootstrapRequired\":true");
        json.ShouldContain("\"DerivedMismatch\":true");
        json.ShouldContain("DeadLetter");
        json.ShouldNotContain("Passenger");
    }
}
