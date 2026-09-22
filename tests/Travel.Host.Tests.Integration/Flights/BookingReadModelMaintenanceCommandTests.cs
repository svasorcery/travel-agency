using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Host.Commands;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Runtime;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class BookingReadModelMaintenanceCommandTests
{
    [Fact]
    public async Task Real_process_validates_without_consuming_a_persisted_message_or_starting_web()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var pg = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await pg.StartAsync(ct);
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddBookingReadModelMaintenance(pg.GetConnectionString(), false);
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(pg.GetConnectionString());
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options => options.Discovery.DisableConventionalDiscovery());
        using var setup = builder.Build();
        await using (var scope = setup.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<FlightsDbContext>()
                .Database.MigrateAsync(ct);
        var store = setup.Services.GetRequiredService<IDocumentStore>();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All);
        var runtime = setup.Services.GetRequiredService<IWolverineRuntime>();
        await runtime.Storage.Admin.MigrateAsync();
        var pending = new Envelope(new ReconcileOrderReadModel(Guid.NewGuid()))
        {
            Id = Guid.NewGuid(),
            Data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new ReconcileOrderReadModel(Guid.NewGuid())
            ),
            MessageType = typeof(ReconcileOrderReadModel).FullName,
            Destination = new Uri("local://flights-booking-reconcile"),
            Status = EnvelopeStatus.Incoming,
            OwnerId = 0,
        };
        await runtime.Storage.Inbox.StoreIncomingAsync(pending);
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(typeof(BookingReadModelCommand).Assembly.Location);
        start.ArgumentList.Add("booking-read-model");
        start.ArgumentList.Add("validate");
        start.Environment["ConnectionStrings__travel"] = pg.GetConnectionString();
        start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        // Any attempt to start the normal web path must fail instead of binding a listener.
        start.Environment["ASPNETCORE_URLS"] = "invalid-web-address";
        var cases = new (string[] Args, int Exit, string Text)[]
        {
            (["validate"], 0, "\"Failed\":0"),
            (["rebuild", "--execute", "--exclusive-maintenance"], 0, "\"Failed\":0"),
            (["inspect", "--aggregate-id", Guid.NewGuid().ToString()], 1, "SourceMissing"),
            (
                ["replay", "--message-id", Guid.NewGuid().ToString(), "--execute"],
                1,
                "DeadLetterNotFound"
            ),
        };
        foreach (var testCase in cases)
        {
            start.ArgumentList.Clear();
            start.ArgumentList.Add(typeof(BookingReadModelCommand).Assembly.Location);
            start.ArgumentList.Add("booking-read-model");
            foreach (var argument in testCase.Args)
                start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(45), ct);
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            var output = await stdout;
            var error = await stderr;
            process.ExitCode.ShouldBe(testCase.Exit, output + error);
            output.ShouldContain(testCase.Text);
            (await runtime.Storage.Admin.AllIncomingAsync())
                .Single(x => x.Id == pending.Id)
                .Status.ShouldBe(EnvelopeStatus.Incoming);
            (
                await runtime.Storage.DeadLetters.DeadLetterEnvelopeByIdAsync(pending.Id)
            ).ShouldBeNull();
        }
    }

    [Theory]
    [InlineData("rebuild")]
    [InlineData("rebuild", "--execute")]
    [InlineData("replay", "--execute", "--message-id", "not-a-guid")]
    [InlineData("validate", "--execute")]
    public async Task Invalid_or_unapproved_arguments_fail_before_connecting(params string[] args)
    {
        using var output = new StringWriter();
        var config = new ConfigurationBuilder().Build();
        (
            await BookingReadModelCommand.RunAsync(
                args,
                output,
                config,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(2);
        output.ToString().ShouldContain("InvalidArguments");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Missing_schema_is_a_prerequisite_failure_and_creates_nothing(
        string environment
    )
    {
        await using var pg = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await pg.StartAsync(TestContext.Current.CancellationToken);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:travel"] = pg.GetConnectionString(),
                    ["environment"] = environment,
                }
            )
            .Build();
        using var output = new StringWriter();
        (
            await BookingReadModelCommand.RunAsync(
                ["validate"],
                output,
                config,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(3);
        output.ToString().ShouldContain("SchemaPrerequisite");
        await using var connection = new NpgsqlConnection(pg.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "select count(*) from information_schema.tables where table_schema not in ('pg_catalog', 'information_schema')",
            connection
        );
        ((long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBe(
            0
        );
    }
}
