using System.Security.Cryptography.X509Certificates;

namespace Tessera.Net.Tls;

/// <summary>
/// The trust anchor set used to validate server certificate chains. Backed by
/// the bundled CCADB PEM so verification is deterministic across platforms
/// rather than dependent on the OS trust store.
/// </summary>
public sealed class RootCertificates
{
    private const string ResourceSuffix = ".Resources.Roots.ccadb.pem";

    private RootCertificates(X509Certificate2Collection certificates)
    {
        Certificates = certificates;
    }

    public static RootCertificates Default { get; } = LoadDefault();

    /// <summary>
    /// The trust anchors, suitable for use as an <see cref="X509ChainPolicy"/>
    /// custom trust store.
    /// </summary>
    public X509Certificate2Collection Certificates { get; }

    public static RootCertificates FromPem(Stream pemStream)
    {
        if (pemStream is null) throw new ArgumentNullException(nameof(pemStream));

        using var reader = new StreamReader(pemStream);
        var pem = reader.ReadToEnd();

        var certificates = new X509Certificate2Collection();
        certificates.ImportFromPem(pem);
        if (certificates.Count == 0)
            throw new InvalidDataException("root certificate bundle is empty");

        return new RootCertificates(certificates);
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
