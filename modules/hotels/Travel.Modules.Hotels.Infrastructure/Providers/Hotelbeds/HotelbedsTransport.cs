using System.Security.Cryptography.X509Certificates;

namespace Travel.Modules.Hotels.Infrastructure.Providers.Hotelbeds;

public static class HotelbedsTransport
{
    public static HttpMessageHandler CreateHandler(HotelbedsOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            MaxConnectionsPerServer = 2,
            MaxResponseHeadersLength = 16,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        // Certificate access occurs only at an actual permitted TLS request, never while the
        // disabled/unapproved provider is being composed. New reads/purchases are gated by the
        // adapter/application; an already admitted matching reader retains its transport profile.
        if (
            !options.Fictional
            && options.ClientCertificateThumbprint is { Length: 40 or 64 } thumbprint
        )
        {
            handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) =>
                SelectCertificate(thumbprint);
        }
        return handler;
    }

    private static X509Certificate SelectCertificate(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            thumbprint,
            validOnly: true
        );
        if (found.Count != 1 || !found[0].HasPrivateKey)
            throw new InvalidOperationException("Hotelbeds client certificate unavailable.");
        return found[0];
    }
}
