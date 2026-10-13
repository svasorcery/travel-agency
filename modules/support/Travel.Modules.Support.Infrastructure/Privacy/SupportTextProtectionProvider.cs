using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using ErrorOr;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Support.Infrastructure.Privacy;

public sealed class SupportTextProtectionProvider(
    IOptions<SupportTextProtectionOptions> options,
    TimeProvider time
) : IDisposable
{
    public const string ApplicationName = "Travel.Support.Text.v1";
    private readonly object gate = new();
    private readonly List<X509Certificate2> certificates = [];
    private ServiceProvider? services;
    private SupportTextProtectionOptions Settings => options.Value;
    public SupportProtectionAvailability Availability
    {
        get
        {
            lock (gate)
            {
                if (
                    string.IsNullOrWhiteSpace(Settings.KeyRingPath)
                    && string.IsNullOrWhiteSpace(Settings.ActiveCertificatePath)
                )
                    return SupportProtectionAvailability.NotConfigured;
                try
                {
                    RequireKeys();
                    return SupportProtectionAvailability.Ready;
                }
                catch (Exception ex) when (IsProtectionFailure(ex))
                {
                    return SupportProtectionAvailability.Unavailable;
                }
            }
        }
    }

    public ErrorOr<Success> Initialize()
    {
        lock (gate)
        {
            try
            {
                ValidatePaths();
                if (
                    Directory.Exists(Settings.KeyRingPath)
                    && Directory
                        .EnumerateFileSystemEntries(Settings.KeyRingPath!)
                        .Any(p => Path.GetFileName(p) != ".initialize.lock")
                )
                    return Error.Conflict(
                        "Support.KeysAlreadyInitialized",
                        "Support keys already initialized."
                    );
                var container = Services(createDirectory: true);
                // Cross-process exclusion; retain the empty lock file to avoid unlink/open races.
                using var lease = new FileStream(
                    Path.Combine(Settings.KeyRingPath!, ".initialize.lock"),
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
                var manager = container.GetRequiredService<IKeyManager>();
                if (
                    manager.GetAllKeys().Count != 0
                    || Directory
                        .EnumerateFileSystemEntries(Settings.KeyRingPath!)
                        .Any(p => Path.GetFileName(p) != ".initialize.lock")
                )
                    return Error.Conflict(
                        "Support.KeysAlreadyInitialized",
                        "Support keys already initialized."
                    );
                var now = time.GetUtcNow();
                manager.CreateNewKey(now.AddMinutes(-1), now.AddDays(90));
                return Result.Success;
            }
            catch (Exception ex) when (IsProtectionFailure(ex))
            {
                return Error.Failure(
                    "Support.ProtectionUnavailable",
                    "Support text protection is unavailable."
                );
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
                return Error.Failure(
                    "Support.ProtectionUnavailable",
                    "Support text protection is unavailable."
                );
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
                return Error.Failure(
                    "Support.ProtectionUnavailable",
                    "Support text protection is unavailable."
                );
            }
        }
    }

    private ServiceProvider RequireKeys()
    {
        ValidatePaths();
        if (!Directory.Exists(Settings.KeyRingPath))
            throw new InvalidOperationException();
        var container = Services(false);
        var key = container
            .GetRequiredService<IKeyManager>()
            .GetAllKeys()
            .Where(k => !k.IsRevoked)
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

    private ServiceProvider Services(bool createDirectory)
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
            if (createDirectory)
                Directory.CreateDirectory(Settings.KeyRingPath!);
            var collection = new ServiceCollection();
            // Isolated logger factory has no providers; dependency errors never log secrets.
            collection.AddLogging();
            collection
                .AddDataProtection()
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
