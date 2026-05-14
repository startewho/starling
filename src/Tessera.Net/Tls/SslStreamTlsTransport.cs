using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Tessera.Common;
using Tessera.Net.Tcp;

namespace Tessera.Net.Tls;

/// <summary>
/// TLS transport built on the BCL <see cref="SslStream"/> (OS TLS stack).
/// Pure-managed at the project level — <see cref="SslStream"/> is part of the
/// .NET BCL — so <c>Tessera.Net</c> keeps its P/Invoke-free bill of health.
/// </summary>
/// <remarks>
/// Certificate validation does not consult the OS trust store: the
/// <see cref="RemoteCertificateValidationCallback"/> routes the presented
/// chain through <see cref="CertificateVerifier"/>, which builds against the
/// bundled CCADB roots for cross-platform determinism.
/// </remarks>
public sealed class SslStreamTlsTransport : ITlsTransport
{
    private readonly SslStream _sslStream;
    private readonly TcpConnectionStream _tcpStream;
    private bool _disposed;

    private SslStreamTlsTransport(
        SslStream sslStream,
        TcpConnectionStream tcpStream,
        string? negotiatedApplicationProtocol)
    {
        _sslStream = sslStream;
        _tcpStream = tcpStream;
        NegotiatedApplicationProtocol = negotiatedApplicationProtocol;
    }

    public Stream Stream => _sslStream;
    public string? NegotiatedApplicationProtocol { get; }

    public static async Task<Result<SslStreamTlsTransport, TlsError>> ConnectAsync(
        ITcpConnection tcpConnection,
        TlsClientOptions options,
        CancellationToken ct = default)
    {
        if (tcpConnection is null) throw new ArgumentNullException(nameof(tcpConnection));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServerName) || options.ApplicationProtocols.Count == 0)
            return Result<SslStreamTlsTransport, TlsError>.Err(TlsError.InvalidOptions);

        var tcpStream = new TcpConnectionStream(tcpConnection);

        // Tracks whether a handshake failure was caused by our custom
        // certificate verification rejecting the chain, so the caller can
        // surface CertificateRejected vs. a generic HandshakeFailed.
        var certificateRejected = false;

        var sslStream = new SslStream(
            tcpStream,
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, chain, _) =>
            {
                if (certificate is null)
                {
                    certificateRejected = true;
                    return false;
                }

                using var leaf = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
                X509Certificate2Collection? extras = null;
                if (chain is not null && chain.ChainElements.Count > 1)
                {
                    extras = new X509Certificate2Collection();
                    // Skip element 0 (the leaf); the rest are presented intermediates.
                    for (var i = 1; i < chain.ChainElements.Count; i++)
                        extras.Add(chain.ChainElements[i].Certificate);
                }

                var ok = CertificateVerifier.Verify(
                    leaf,
                    extras,
                    options.ServerName,
                    RootCertificates.Default,
                    options.ValidationTime);
                if (!ok)
                    certificateRejected = true;
                return ok;
            });

        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = options.ServerName,
            EnabledSslProtocols = SslProtocols.Tls13,
            ApplicationProtocols = options.ApplicationProtocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList(),
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        try
        {
            await sslStream.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);

            var negotiated = sslStream.NegotiatedApplicationProtocol;
            string? negotiatedName = negotiated.Protocol.IsEmpty
                ? null
                : System.Text.Encoding.ASCII.GetString(negotiated.Protocol.Span);

            return Result<SslStreamTlsTransport, TlsError>.Ok(
                new SslStreamTlsTransport(sslStream, tcpStream, negotiatedName));
        }
        catch (AuthenticationException) when (certificateRejected)
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return Result<SslStreamTlsTransport, TlsError>.Err(TlsError.CertificateRejected);
        }
        catch
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return certificateRejected
                ? Result<SslStreamTlsTransport, TlsError>.Err(TlsError.CertificateRejected)
                : Result<SslStreamTlsTransport, TlsError>.Err(TlsError.HandshakeFailed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sslStream.Dispose();
        _tcpStream.Dispose();
    }
}
