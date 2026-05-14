namespace Travel.Modules.Flights.Application.Queries;

/// <summary>
/// Query dispatched by API endpoints that accept free-form natural-language flight queries.
/// </summary>
public sealed record NlSearchQuery(string Query, string Locale = "ru");
