namespace Travel.Host.Features.Status;

public sealed record StatusResponse(string Version, string Db, DateTimeOffset Timestamp);
