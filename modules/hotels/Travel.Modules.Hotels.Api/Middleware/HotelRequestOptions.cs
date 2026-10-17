namespace Travel.Modules.Hotels.Api.Middleware;

public sealed class HotelRequestOptions
{
    public int RequestBytes { get; set; } = 16 * 1024;

    public bool IsValid() => RequestBytes is >= 256 and <= 16 * 1024;
}
