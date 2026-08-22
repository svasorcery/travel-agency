using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Travel.ServiceDefaults.Web;

public static class PlatformProblemDetails
{
    public static void Configure(ProblemDetailsOptions options)
    {
        options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
    }
}
