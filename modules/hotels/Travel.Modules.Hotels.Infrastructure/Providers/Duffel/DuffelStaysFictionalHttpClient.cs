namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

public interface IDuffelStaysFictionalTransport;

public sealed class DuffelStaysFictionalHttpClient : HttpClient
{
    public DuffelStaysFictionalHttpClient(HttpMessageHandler handler)
        : base(handler)
    {
        if (handler is not IDuffelStaysFictionalTransport)
            throw new ArgumentException(
                "An owned fictional transport is required.",
                nameof(handler)
            );
        Timeout = System.Threading.Timeout.InfiniteTimeSpan;
    }
}
