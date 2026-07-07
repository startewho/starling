using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Starling.Common.Diagnostics;
using Starling.Net.Tls;

namespace Starling.Net.Http;

/// <summary>
/// Opens the TCP + TLS connections behind a <see cref="System.Net.Http.HttpClient"/>.
/// Wired in as <see cref="SocketsHttpHandler.ConnectCallback"/> so we keep three
/// things the browser needs that the default connect path hides: our bundled
/// trust anchors (the OS store is not the authority), the verified leaf
/// certificate surfaced to the shell lock UI, and per-phase (dns / tcp / tls)
/// telemetry spans.
/// </summary>
internal sealed class SecureConnector
{
    private readonly RootCertificates _roots;
    private readonly List<SslApplicationProtocol> _alpn;
    // Latest verified leaf per origin ("host:port"). A connection is only
    // recorded after its chain validated, so a present entry always describes a
    // certificate that passed CertificateVerifier. Reused across the origin's
    // pooled connections, which all present the same certificate.
    private readonly ConcurrentDictionary<string, CertificateSummary> _certificates = new(StringComparer.Ordinal);

    public SecureConnector(RootCertificates roots, IReadOnlyList<string> alpnProtocols)
    {
        _roots = roots;
        _alpn = alpnProtocols.Select(p => new SslApplicationProtocol(p)).ToList();
    }

    public CertificateSummary? CertificateFor(string host, int port) =>
        _certificates.TryGetValue(Key(host, port), out var summary) ? summary : null;

    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var isHttps = context.InitialRequestMessage.RequestUri is { Scheme: "https" };

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            IPAddress[] addresses;
            using (StarlingTelemetry.Span("net", "dns"))
            {
                StarlingTelemetry.Counter("net.dns.resolutions", 1);
                addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            }

            using (StarlingTelemetry.Span("net", "tcp_connect"))
            {
                StarlingTelemetry.Counter("net.tcp.connects", 1);
                await socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
            }

            var networkStream = new NetworkStream(socket, ownsSocket: true);
            if (!isHttps)
            {
                return networkStream;
            }

            var ssl = new SslStream(networkStream, leaveInnerStreamOpen: false);
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12,
                ApplicationProtocols = _alpn,
                RemoteCertificateValidationCallback = (_, cert, chain, _) => Verify(host, cert, chain),
            };

            try
            {
                using (StarlingTelemetry.Span("net", "tls_handshake"))
                {
                    StarlingTelemetry.Counter("net.tls.handshakes", 1);
                    await ssl.AuthenticateAsClientAsync(sslOptions, ct).ConfigureAwait(false);
                }
            }
            catch
            {
                StarlingTelemetry.Counter("net.tls.failures", 1);
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            if (ssl.RemoteCertificate is { } leaf)
            {
                _certificates[Key(host, port)] = CertificateVerifier.Summarize(AsCertificate2(leaf));
            }
            return ssl;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private bool Verify(string host, X509Certificate? cert, X509Chain? chain)
    {
        if (cert is null)
        {
            return false;
        }

        X509Certificate2Collection? intermediates = null;
        if (chain is { ChainElements.Count: > 0 })
        {
            intermediates = new X509Certificate2Collection();
            foreach (var element in chain.ChainElements)
            {
                intermediates.Add(element.Certificate);
            }
        }

        return CertificateVerifier.Verify(AsCertificate2(cert), intermediates, host, _roots);
    }

    private static X509Certificate2 AsCertificate2(X509Certificate cert) =>
        cert as X509Certificate2 ?? X509CertificateLoader.LoadCertificate(cert.Export(X509ContentType.Cert));

    private static string Key(string host, int port) => $"{host}:{port}";
}
