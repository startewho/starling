using System.Security.Cryptography.X509Certificates;

namespace Tessera.Net.Tls;

/// <summary>
/// Validates a server certificate chain against the bundled CCADB trust
/// anchors. Chain building, expiry, and signature checks run through
/// <see cref="X509Chain"/> with a <see cref="X509ChainPolicy.CustomTrustStore"/>
/// so the OS trust store is never consulted; host-name matching stays as
/// custom RFC 6125 code in <see cref="CertificateHostNameMatcher"/>.
/// </summary>
public static class CertificateVerifier
{
    /// <summary>
    /// Verifies <paramref name="leafCertificate"/> (plus any intermediates the
    /// server presented in <paramref name="extraCertificates"/>) chains to a
    /// bundled trust anchor and matches <paramref name="hostname"/>.
    /// </summary>
    public static bool Verify(
        X509Certificate2 leafCertificate,
        X509Certificate2Collection? extraCertificates,
        string hostname,
        RootCertificates roots,
        DateTimeOffset? validationTime = null)
    {
        if (leafCertificate is null) throw new ArgumentNullException(nameof(leafCertificate));
        if (roots is null) throw new ArgumentNullException(nameof(roots));
        if (string.IsNullOrWhiteSpace(hostname)) return false;

        if (!CertificateHostNameMatcher.Matches(leafCertificate, hostname))
            return false;

        using var chain = new X509Chain();
        var policy = chain.ChainPolicy;
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.CustomTrustStore.Clear();
        policy.CustomTrustStore.AddRange(roots.Certificates);
        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (validationTime is { } when)
            policy.VerificationTime = when.UtcDateTime;
        if (extraCertificates is { Count: > 0 })
            policy.ExtraStore.AddRange(extraCertificates);

        return chain.Build(leafCertificate);
    }
}

/// <summary>
/// RFC 6125 host-name matching against a certificate's DNS Subject Alternative
/// Names, including single-label wildcard support.
/// </summary>
public static class CertificateHostNameMatcher
{
    public static bool Matches(X509Certificate2 certificate, string hostname)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        if (string.IsNullOrWhiteSpace(hostname)) return false;

        var normalizedHost = hostname.Trim().TrimEnd('.').ToLowerInvariant();

        foreach (var dnsName in EnumerateDnsNames(certificate))
        {
            if (MatchDnsName(dnsName, normalizedHost))
                return true;
        }

        return false;
    }

    public static bool MatchDnsName(string pattern, string hostname)
    {
        var normalizedPattern = pattern.Trim().TrimEnd('.').ToLowerInvariant();
        var normalizedHost = hostname.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalizedPattern.Length == 0 || normalizedHost.Length == 0)
            return false;
        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
            return normalizedPattern == normalizedHost;

        if (!normalizedPattern.StartsWith("*.", StringComparison.Ordinal)
            || normalizedPattern.IndexOf('*', 1) >= 0)
            return false;

        var suffix = normalizedPattern[1..];
        if (!normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var unmatched = normalizedHost[..^suffix.Length];
        return unmatched.Length > 0 && !unmatched.Contains('.', StringComparison.Ordinal);
    }

    private static IEnumerable<string> EnumerateDnsNames(X509Certificate2 certificate)
    {
        foreach (var extension in certificate.Extensions)
        {
            if (extension is not X509SubjectAlternativeNameExtension san)
                continue;
            foreach (var dnsName in san.EnumerateDnsNames())
                yield return dnsName;
        }
    }
}
