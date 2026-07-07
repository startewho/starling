using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Starling.Net.Tls;

internal static partial class SystemRootCertificatesLog
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "OS Root store unavailable for {StoreLocation}")]
    public static partial void StoreUnavailable(ILogger logger, Exception ex, string storeLocation);
}

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
    public static IReadOnlyList<X509Certificate2> Load(ILogger? log = null)
    {
        log ??= NullLogger.Instance;
        var certificates = new List<X509Certificate2>();

        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            try
            {
                using var store = new X509Store(StoreName.Root, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                foreach (var osCertificate in store.Certificates)
                {
                    certificates.Add(osCertificate);
                }
            }
            catch (Exception ex)
            {
                // Store unavailable on this platform/scope — fall through.
                SystemRootCertificatesLog.StoreUnavailable(log, ex, location.ToString());
            }
        }

        return certificates;
    }
}
