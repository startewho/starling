using System.Security.Cryptography.X509Certificates;

namespace Starling.Net.Tls;

/// <summary>
/// The trust anchors Starling chains server certificates to. Backed by the
/// embedded CCADB bundle so trust decisions are deterministic across machines,
/// independent of the OS trust store.
/// </summary>
public sealed class RootCertificates
{
    private const string ResourceSuffix = ".Resources.Roots.ccadb.pem";
    private readonly X509Certificate2Collection _certificates;

    private RootCertificates(X509Certificate2Collection certificates)
    {
        _certificates = certificates;
    }

    /// <summary>
    /// The embedded CCADB bundle only. Deterministic across machines — use this
    /// for tests and anywhere reproducible trust decisions matter.
    /// </summary>
    public static RootCertificates Default { get; } = LoadDefault();

    /// <summary>
    /// The embedded bundle plus any roots the operating system trusts (corporate
    /// internal CAs, mkcert, debugging proxies). The embedded bundle is the floor,
    /// so missing or stale OS roots can never make a publicly-valid site fail.
    /// This is what live connections use.
    /// </summary>
    public static RootCertificates SystemTrust { get; } = BuildSystemTrust();

    /// <summary>
    /// The trust anchors as a collection suitable for
    /// <see cref="X509ChainPolicy.CustomTrustStore"/>.
    /// </summary>
    public X509Certificate2Collection Certificates => _certificates;

    public static RootCertificates FromPem(Stream pemStream)
    {
        if (pemStream is null)
        {
            throw new ArgumentNullException(nameof(pemStream));
        }

        using var reader = new StreamReader(pemStream);
        var pem = reader.ReadToEnd();
        var certificates = new X509Certificate2Collection();
        certificates.ImportFromPem(pem);
        if (certificates.Count == 0)
        {
            throw new InvalidDataException("root certificate bundle is empty");
        }

        return new RootCertificates(certificates);
    }

    private static RootCertificates BuildSystemTrust()
    {
        var combined = new X509Certificate2Collection();
        combined.AddRange(Default._certificates);

        // Dedup by thumbprint so a CA present in both the bundle and the OS store
        // becomes a single trust anchor.
        var seen = new HashSet<string>(Default._certificates.Count, StringComparer.Ordinal);
        foreach (var certificate in Default._certificates)
        {
            seen.Add(certificate.Thumbprint);
        }

        foreach (var certificate in SystemRootCertificates.Load())
        {
            if (seen.Add(certificate.Thumbprint))
            {
                combined.Add(certificate);
            }
        }

        return new RootCertificates(combined);
    }

    private static RootCertificates LoadDefault()
    {
        var assembly = typeof(RootCertificates).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded root store resource: {resourceName}");
        return FromPem(stream);
    }
}
