using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Shared.Web;

namespace Travel.Modules.Flights.Api.Endpoints;

internal static class CustomerRefundEndpointResults
{
    internal static IResult Reply<T>(ErrorOr<T> result, HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        return result.IsError
            ? Results.Problem(result.Errors.ToProblemDetails())
            : Results.Ok(result.Value);
    }
}
