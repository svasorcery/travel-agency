using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Modules.Flights.Application.ReadModels;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public static class BookingStorageFailureClassifier
{
    public static Exception Classify(Exception error, CancellationToken ct)
    {
        if (error is OperationCanceledException && ct.IsCancellationRequested)
            return error;
        if (error is BookingProjectionTransientException or BookingProjectionTerminalException)
            return error;
        for (Exception? cause = error; cause is not null; cause = cause.InnerException)
        {
            if (cause is DbUpdateConcurrencyException)
                return new BookingProjectionTransientException("ConcurrentProjectionWrite", error);
            if (cause is PostgresException postgres)
            {
                if (
                    postgres.SqlState is "40001" or "40P01" or "55P03"
                    || (
                        postgres.SqlState == "23505"
                        && postgres.ConstraintName == "ix_order_read_model_aggregate_id"
                    )
                )
                    return new BookingProjectionTransientException(
                        "ConcurrentProjectionWrite",
                        error
                    );
                if (postgres.SqlState is "42P01" or "42703" or "3F000")
                    return new BookingProjectionTerminalException(
                        "ProjectionSchemaIncompatible",
                        error
                    );
                if (postgres.SqlState.StartsWith("23", StringComparison.Ordinal))
                    return new BookingProjectionTerminalException(
                        "ProjectionIntegrityViolation",
                        error
                    );
            }
            if (cause is NpgsqlException { IsTransient: true })
                return new BookingProjectionTransientException(
                    "ProjectionStorageUnavailable",
                    error
                );
        }
        return error;
    }
}
