using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.X509;
using BcCertificate = Org.BouncyCastle.X509.X509Certificate;

namespace Starling.Net.Tls;

/// <summary>
/// Reads root certificates the operating system trusts (the platform "Root"
/// store, both machine- and user-scoped). This is how admin- or user-installed
/// CAs — corporate internal roots, <c>mkcert</c>, debugging proxies — become
/// trusted on a managed machine without shipping them in our embedded bundle.
///
/// Best-effort: the store may be empty or unavailable (notably on Linux, where
/// .NET surfaces no managed Root store), so failures yield an empty list rather
/// than throwing. The embedded bundle remains the floor either way.
/// </summary>
internal static class SystemRootCertificates
{
    public static IReadOnlyList<BcCertificate> Load()
    {
        var parser = new X509CertificateParser();
        var certificates = new List<BcCertificate>();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.Root, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var osCertificate in store.Certificates)
                {
                    try
                    {
                        certificates.Add(parser.ReadCertificate(osCertificate.RawData));
                    }
                    catch
                    {
                        // Skip any entry BouncyCastle can't parse; one bad cert
                        // must not poison the whole store.
                    }
                }
            }
            catch
            {
                // Store unavailable on this platform/scope — fall through.
            }
        }

        return certificates;
    }
}
