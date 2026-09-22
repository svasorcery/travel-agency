using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;

namespace Travel.Modules.Flights.Tests.Unit.Persistence;

public sealed class BookingStorageFailureClassifierTests
{
    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    [InlineData("55P03")]
    public void Wrapped_postgres_transient_failures_are_retryable(string state)
    {
        var error = new DbUpdateException(
            "save",
            new PostgresException("database", "ERROR", "ERROR", state)
        );
        BookingStorageFailureClassifier
            .Classify(error, TestContext.Current.CancellationToken)
            .ShouldBeOfType<BookingProjectionTransientException>()
            .InnerException.ShouldBe(error);
    }

    [Theory]
    [InlineData("ix_order_read_model_aggregate_id", true)]
    [InlineData("pk_order_read_model", false)]
    public void Only_the_aggregate_unique_race_is_retryable(string constraint, bool transient)
    {
        var error = new DbUpdateException(
            "save",
            new PostgresException(
                "duplicate",
                "ERROR",
                "ERROR",
                "23505",
                constraintName: constraint
            )
        );
        var result = BookingStorageFailureClassifier.Classify(
            error,
            TestContext.Current.CancellationToken
        );
        if (transient)
            result.ShouldBeOfType<BookingProjectionTransientException>();
        else
            result.ShouldBeOfType<BookingProjectionTerminalException>();
    }

    [Fact]
    public void Wrapped_network_timeout_is_transient_and_schema_error_is_terminal()
    {
        var timeout = new DbUpdateException(
            "save",
            new NpgsqlException("network", new TimeoutException())
        );
        BookingStorageFailureClassifier
            .Classify(timeout, TestContext.Current.CancellationToken)
            .ShouldBeOfType<BookingProjectionTransientException>();
        var missingTable = new DbUpdateException(
            "save",
            new PostgresException("missing", "ERROR", "ERROR", "42P01")
        );
        BookingStorageFailureClassifier
            .Classify(missingTable, TestContext.Current.CancellationToken)
            .ShouldBeOfType<BookingProjectionTerminalException>()
            .Message.ShouldBe("ProjectionSchemaIncompatible");
    }

    [Fact]
    public void Cancellation_and_unknown_errors_are_preserved()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = new OperationCanceledException(cts.Token);
        BookingStorageFailureClassifier.Classify(cancelled, cts.Token).ShouldBeSameAs(cancelled);
        var unknown = new InvalidOperationException("unexpected");
        BookingStorageFailureClassifier
            .Classify(unknown, TestContext.Current.CancellationToken)
            .ShouldBeSameAs(unknown);
        BookingStorageFailureClassifier
            .Classify(new DbUpdateConcurrencyException(), TestContext.Current.CancellationToken)
            .ShouldBeOfType<BookingProjectionTransientException>();
    }
}
