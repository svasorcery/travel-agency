using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class TripsProtectionProvider(
    IOptions<TripsProtectionOptions> options,
    TimeProvider time
) : IDisposable
{
    public const string ApplicationName = "Travel.Trips.Plans.v1";
    private readonly object gate = new();
    private readonly List<X509Certificate2> certificates = [];
    private ServiceProvider? services;
    private TripsProtectionOptions Settings => options.Value;
    public bool IsReady
    {
        get
        {
            lock (gate)
            {
                try
                {
                    RequireKeys();
                    return true;
                }
                catch (Exception ex) when (IsProtectionFailure(ex))
                {
                    return false;
                }
            }
        }
    }

    public ErrorOr<byte[]> Protect(byte[] payload, params string[] purposes)
    {
        lock (gate)
        {
            try
            {
                var container = RequireKeys();
                return Protector(container, purposes).Protect(payload);
            }
            catch (Exception ex) when (IsProtectionFailure(ex))
            {
                return Error.Failure("Trips.Unavailable", "Trips is unavailable.");
            }
        }
    }

    public ErrorOr<byte[]> Unprotect(byte[] payload, params string[] purposes)
    {
        lock (gate)
        {
            try
            {
                var container = RequireKeys();
                return Protector(container, purposes).Unprotect(payload);
            }
            catch (Exception ex) when (IsProtectionFailure(ex))
            {
                return Error.Failure("Trips.Unavailable", "Trips is unavailable.");
            }
        }
    }

    private ServiceProvider RequireKeys()
    {
        ValidatePaths();
        if (!Directory.Exists(Settings.KeyRingPath))
            throw new InvalidOperationException();
        var container = Services();
        var key = container
            .GetRequiredService<IKeyManager>()
            .GetAllKeys()
            .Where(k =>
                !k.IsRevoked
                && k.ActivationDate <= time.GetUtcNow()
                && k.ExpirationDate > time.GetUtcNow()
            )
            .OrderByDescending(k => k.ActivationDate)
            .FirstOrDefault();
        if (key is null || key.CreateEncryptor() is null)
            throw new InvalidOperationException();
        return container;
    }

    private static IDataProtector Protector(IServiceProvider container, string[] purposes)
    {
        if (purposes.Length == 0 || purposes.Any(string.IsNullOrEmpty))
            throw new ArgumentException();
        return container
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(purposes[0], purposes.Skip(1).ToArray());
    }

    private void ValidatePaths()
    {
        if (
            string.IsNullOrWhiteSpace(Settings.KeyRingPath)
            || !Path.IsPathFullyQualified(Settings.KeyRingPath)
            || Path.GetFullPath(Settings.KeyRingPath).TrimEnd(Path.DirectorySeparatorChar)
                == Path.GetPathRoot(Settings.KeyRingPath)?.TrimEnd(Path.DirectorySeparatorChar)
            || string.IsNullOrWhiteSpace(Settings.ActiveCertificatePath)
            || !Path.IsPathFullyQualified(Settings.ActiveCertificatePath)
        )
            throw new InvalidOperationException();
    }

    private ServiceProvider Services()
    {
        if (services is not null)
            return services;
        ValidatePaths();
        var loaded = new List<X509Certificate2>();
        try
        {
            X509Certificate2 Load(string path, string? password)
            {
                if (!Path.IsPathFullyQualified(path))
                    throw new InvalidOperationException();
                var cert = X509CertificateLoader.LoadPkcs12FromFile(
                    path,
                    password,
                    X509KeyStorageFlags.EphemeralKeySet
                );
                loaded.Add(cert);
                if (!cert.HasPrivateKey)
                    throw new InvalidOperationException();
                return cert;
            }
            var active = Load(Settings.ActiveCertificatePath!, Settings.ActiveCertificatePassword);
            var readers = (Settings.ReadCertificates ?? [])
                .Select(c =>
                    c is null ? throw new InvalidOperationException() : Load(c.Path, c.Password)
                )
                .Prepend(active)
                .ToArray();
            var collection = new ServiceCollection();
            // Isolated logger factory has no providers; dependency errors never log secrets.
            collection.AddLogging();
            collection
                .AddDataProtection()
                .DisableAutomaticKeyGeneration()
                .SetApplicationName(ApplicationName)
                .PersistKeysToFileSystem(new DirectoryInfo(Settings.KeyRingPath!))
                .ProtectKeysWithCertificate(active)
                .UnprotectKeysWithAnyCertificate(readers);
            services = collection.BuildServiceProvider();
            certificates.AddRange(loaded);
            return services;
        }
        catch
        {
            foreach (var cert in loaded)
                cert.Dispose();
            throw;
        }
    }

    private static bool IsProtectionFailure(Exception ex) =>
        ex
            is CryptographicException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException
                or XmlException
                or System.Security.SecurityException;

    public void Dispose()
    {
        lock (gate)
        {
            services?.Dispose();
            foreach (var certificate in certificates)
                certificate.Dispose();
            certificates.Clear();
            services = null;
        }
    }
}
