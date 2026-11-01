namespace Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

public sealed class HotelbedsOptions
{
    public bool Enabled { get; set; }
    public bool BookingEnabled { get; set; }
    public bool Fictional { get; set; }
    public bool ExternalUseApproved { get; set; }
    public int[]? FictionalHotelIds { get; set; } = [900001];
    public string AccountAlias { get; set; } = "";
    public string ConfigurationRevision { get; set; } = "";
    public string ApprovalRevision { get; set; } = "";
    public string PricingProfile { get; set; } = "Net";
    public string? ApiKey { get; set; }
    public string? Secret { get; set; }
    public string? ClientCertificateThumbprint { get; set; }

    public bool IsValid(bool production) =>
        !Enabled
        || !production
            && AccountAlias.Length is > 0 and <= 80
            && ConfigurationRevision.Length is > 0 and <= 80
            && ApprovalRevision.Length is > 0 and <= 80
            && PricingProfile == "Net"
            && (
                !Fictional
                || FictionalHotelIds is { Length: >= 1 and <= 100 } ids
                    && ids.All(id => id > 0)
                    && ids.Distinct().Count() == ids.Length
            )
            && ApiKey is { Length: >= 8 and <= 256 }
            && Secret is { Length: >= 8 and <= 256 }
            && (
                Fictional
                || ClientCertificateThumbprint is { Length: 40 or 64 }
                    && ClientCertificateThumbprint.All(char.IsAsciiHexDigit)
            );
}

// Only explicitly owned transports can be selected for the fictional execution profile.
public interface IHotelbedsFictionalTransport;

public sealed class HotelbedsFictionalHttpClient : HttpClient
{
    public HotelbedsFictionalHttpClient(HttpMessageHandler handler)
        : base(handler)
    {
        if (handler is not IHotelbedsFictionalTransport)
            throw new ArgumentException(
                "An owned fictional transport is required.",
                nameof(handler)
            );
        Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}
