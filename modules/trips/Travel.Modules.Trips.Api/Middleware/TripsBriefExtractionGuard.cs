using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Api.Middleware;

public static class TripsBriefExtractionGuard
{
    public static async Task HandleAsync(
        HttpContext context,
        TripsBriefExtractionRoute route,
        OwnerIdentity owner,
        RequestDelegate next
    )
    {
        var feature = context.RequestServices.GetRequiredService<TripsBriefExtractionFeature>();
        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
        var anchor = clock.GetUtcNow();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);
        var originalCancellation = context.RequestAborted;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            originalCancellation,
            timeout.Token
        );
        context.RequestAborted = linked.Token;
        try
        {
            if (context.Request.Query.Count != 0 || context.Request.Headers.ContainsKey("If-Match"))
            {
                await TripBriefExtractionDtos
                    .Failure(TripBriefErrors.InvalidInput)
                    .ExecuteAsync(context);
                return;
            }
            if (route == TripsBriefExtractionRoute.Capability)
            {
                if (
                    context.Request.ContentLength > 0
                    || await context.Request.Body.ReadAsync(new byte[1], linked.Token) != 0
                )
                    await TripBriefExtractionDtos
                        .Failure(TripBriefErrors.InvalidInput)
                        .ExecuteAsync(context);
                else
                    await next(context);
                return;
            }
            if (context.Request.ContentLength > TripBriefExtractionDtos.MaximumBodyBytes)
            {
                await TripsProblems.Problem("Trips.BriefTooLarge", 413).ExecuteAsync(context);
                return;
            }
            if (
                !context.Request.HasJsonContentType()
                || !MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var media)
                || media.Charset.HasValue
                    && !string.Equals(
                        media.Charset.Value?.Trim('"'),
                        "utf-8",
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            {
                await TripBriefExtractionDtos
                    .Failure(TripBriefErrors.InvalidInput)
                    .ExecuteAsync(context);
                return;
            }
            var bytes = new byte[TripBriefExtractionDtos.MaximumBodyBytes + 1];
            try
            {
                var length = 0;
                while (length < bytes.Length)
                {
                    var read = await context
                        .Request.Body.ReadAsync(bytes.AsMemory(length), linked.Token)
                        .AsTask()
                        .WaitAsync(linked.Token);
                    if (read == 0)
                        break;
                    length += read;
                }
                if (length > TripBriefExtractionDtos.MaximumBodyBytes)
                {
                    await TripsProblems.Problem("Trips.BriefTooLarge", 413).ExecuteAsync(context);
                    return;
                }
                var input = TripBriefExtractionDtos.Read(
                    bytes.AsMemory(0, length),
                    anchor,
                    anchor.AddSeconds(5)
                );
                if (input.IsError)
                {
                    await TripBriefExtractionDtos.Failure(input.FirstError).ExecuteAsync(context);
                    return;
                }
                if (!feature.Configured)
                {
                    await TripBriefExtractionDtos
                        .Failure(TripBriefErrors.Unavailable)
                        .ExecuteAsync(context);
                    return;
                }
                if (input.Value.ProcessingPolicyVersion != feature.ProcessingPolicyVersion)
                {
                    await TripBriefExtractionDtos
                        .Failure(TripBriefErrors.PolicyChanged)
                        .ExecuteAsync(context);
                    return;
                }
                if (
                    !context
                        .RequestServices.GetRequiredService<TripBriefOwnerAdmission>()
                        .TryAdmit(owner)
                )
                {
                    context.Response.Headers.RetryAfter = "60";
                    await TripBriefExtractionDtos
                        .Failure(TripBriefErrors.Busy)
                        .ExecuteAsync(context);
                    return;
                }
                context.Items[TripBriefExtractionDtos.InputKey] = input.Value;
                await next(context);
            }
            finally
            {
                context.Items.Remove(TripBriefExtractionDtos.InputKey);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !originalCancellation.IsCancellationRequested)
        {
            context.RequestAborted = originalCancellation;
            if (!context.Response.HasStarted)
                await TripBriefExtractionDtos
                    .Failure(TripBriefErrors.Deadline)
                    .ExecuteAsync(context);
        }
        finally
        {
            context.RequestAborted = originalCancellation;
        }
    }
}
