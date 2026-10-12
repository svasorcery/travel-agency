using ErrorOr;

namespace Travel.Modules.Support.Core;

public static class SupportErrors
{
    public static Error InvalidInput =>
        Error.Validation("Support.InvalidInput", "The support request is invalid.");
    public static Error Unauthenticated =>
        Error.Unauthorized("Support.Unauthenticated", "Authentication is required.");
    public static Error Forbidden =>
        Error.Forbidden("Support.Forbidden", "Support access is forbidden.");
    public static Error NotFound =>
        Error.NotFound("Support.NotFound", "The support resource was not found.");
    public static Error Conflict =>
        Error.Conflict("Support.Conflict", "The support action conflicts with retained state.");
    public static Error Unavailable =>
        Error.Failure("Support.Unavailable", "Support is temporarily unavailable.");
    public static Error OrderLinkNotFound =>
        Error.NotFound("Support.OrderLinkNotFound", "The linked order was not found.");
    public static Error OrderLinkUnavailable =>
        Error.Failure(
            "Support.OrderLinkUnavailable",
            "Order ownership verification is temporarily unavailable."
        );
}
