using System.Security.Cryptography.X509Certificates;

namespace Starling.Net.Tls;

/// <summary>
/// Server-certificate verification against the bundled trust anchors. Chains the
/// presented leaf to a root in <see cref="RootCertificates"/> (the OS trust store
/// is not consulted unless folded in via <see cref="RootCertificates.SystemTrust"/>),
/// enforces the validity window and path constraints via <see cref="X509Chain"/>,
/// and matches the requested host against the leaf's subject alternative names.
/// Starling fails closed: a rejected chain aborts the connection.
/// </summary>
public static class CertificateVerifier
{
    public static bool Verify(
        X509Certificate2 leaf,
        X509Certificate2Collection? presentedIntermediates,
        string hostname,
        RootCertificates roots,
        DateTimeOffset? validationTime = null)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(roots);

        if (string.IsNullOrWhiteSpace(hostname))
        {
            return false;
        }

        // RFC 6125 host match. No fall-through to the legacy CN field.
        if (!leaf.MatchesHostname(hostname, allowWildcards: true, allowCommonName: false))
        {
            return false;
        }

        using var chain = new X509Chain();
        var policy = chain.ChainPolicy;
        // Chain to our bundled anchors only — the OS trust store is not consulted.
        policy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        policy.CustomTrustStore.AddRange(roots.Certificates);
        // Live OCSP/CRL would add blocking network I/O on the handshake path.
        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (validationTime is { } when)
        {
            policy.VerificationTime = when.UtcDateTime;
        }
        if (presentedIntermediates is { Count: > 0 })
        {
            policy.ExtraStore.AddRange(presentedIntermediates);
        }

        return chain.Build(leaf);
    }

    /// <summary>
    /// Build a display summary of the leaf certificate for the shell lock UI.
    /// Intended to be called after <see cref="Verify"/> has accepted the chain.
    /// </summary>
    public static CertificateSummary Summarize(X509Certificate2 leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        return new CertificateSummary(
            FriendlyName(leaf.SubjectName, leaf.Subject),
            FriendlyName(leaf.IssuerName, leaf.Issuer),
            leaf.NotBefore.ToUniversalTime(),
            leaf.NotAfter.ToUniversalTime());
    }

    // Prefer the common name; fall back to the organisation, then the full DN.
    private static string FriendlyName(X500DistinguishedName dn, string fullDn)
    {
        foreach (var oid in new[] { "CN", "O" })
        {
            var value = FindRdn(dn, oid);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return fullDn;
    }

    private static string? FindRdn(X500DistinguishedName dn, string oidFriendlyName)
    {
        foreach (var rdn in dn.EnumerateRelativeDistinguishedNames())
        {
            if (string.Equals(rdn.GetSingleElementType().FriendlyName, oidFriendlyName, StringComparison.OrdinalIgnoreCase))
            {
                return rdn.GetSingleElementValue();
            }
        }
        return null;
    }
}
