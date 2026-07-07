using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Common;
using Starling.Common.Diagnostics;
using Starling.Net.Http;
using Starling.Net.Http.Cookies;
using Starling.Net.Tls;
using StarlingUrl = global::Starling.Url.Url;

namespace Starling.Net;

internal static partial class StarlingHttpClientLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "HTTP request failed: {Error}")]
    public static partial void RequestFailed(ILogger logger, string error);
}

/// <summary>
/// Top-level HTTP client. A thin browser-policy layer over .NET's
/// <see cref="System.Net.Http.HttpClient"/> (HTTP/1.1 + HTTP/2 over a
/// <see cref="SocketsHttpHandler"/>). We keep the pieces a browser must own and
/// that the default handler would otherwise do automatically:
/// <list type="bullet">
///   <item>Redirects are <b>not</b> auto-followed — callers observe each hop
///   (<see cref="SocketsHttpHandler.AllowAutoRedirect"/> is false).</item>
///   <item>Cookies come from our <see cref="CookieJar"/>, not
///   <see cref="System.Net.CookieContainer"/> (which lacks PSL / prefix rules).</item>
///   <item>Server certificates chain to our bundled trust anchors via
///   <see cref="SecureConnector"/>, and the verified leaf is surfaced on the
///   response for the shell lock UI.</item>
/// </list>
/// </summary>
public sealed class StarlingHttpClient : IDisposable
{
    private readonly StarlingHttpClientOptions _options;
    private readonly System.Net.Http.HttpClient _http;
    private readonly SecureConnector _connector;
    private readonly ILogger _log;
    private bool _disposed;

    public StarlingHttpClient() : this(new StarlingHttpClientOptions()) { }

    public StarlingHttpClient(StarlingHttpClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        var loggerFactory = options.LoggerFactory ?? NullLoggerFactory.Instance;
        _log = loggerFactory.CreateLogger<StarlingHttpClient>();
        _connector = new SecureConnector(RootCertificates.SystemTrust, options.AlpnProtocols);

        var handler = new SocketsHttpHandler
        {
            // Browser policy lives above the transport — the engine follows
            // redirects, our jar drives cookies. Let .NET only decompress.
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = options.ConnectTimeout,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            EnableMultipleHttp2Connections = true,
            ConnectCallback = _connector.ConnectAsync,
        };

        _http = new System.Net.Http.HttpClient(handler, disposeHandler: true)
        {
            // Per-request timeouts are enforced with a linked CTS in SendAsync so
            // a timeout is distinguishable from caller cancellation.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    /// <summary>The User-Agent presented on the wire. Surfaced so the JS layer
    /// can keep <c>navigator.userAgent</c> in lockstep with what we actually
    /// send (sites compare the two).</summary>
    public string UserAgent => _options.UserAgent;

    public Task<Result<HttpResponse, NetworkError>> GetAsync(string url, CancellationToken ct = default)
    {
        var parsed = global::Starling.Url.UrlParser.Parse(url);
        if (parsed.IsErr)
        {
            return Task.FromResult(Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl));
        }

        return SendAsync(HttpRequest.Get(parsed.Value), ct);
    }

    public Task<Result<HttpResponse, NetworkError>> GetAsync(StarlingUrl url, CancellationToken ct = default)
        => SendAsync(HttpRequest.Get(url), ct);

    public async Task<Result<HttpResponse, NetworkError>> SendAsync(
        HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var url = request.Url;

        if (!url.IsHttp && !url.IsHttps)
        {
            return Result<HttpResponse, NetworkError>.Err(NetworkError.UnsupportedScheme);
        }

        if (string.IsNullOrEmpty(url.Host))
        {
            return Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl);
        }

        var port = url.Port ?? url.DefaultPort ?? (url.IsHttps ? 443 : 80);
        if (port is < 1 or > 65535)
        {
            return Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl);
        }

        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.Set("User-Agent", _options.UserAgent);
        }
        ApplyRequestCookies(request, url);

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestCts.CancelAfter(_options.RequestTimeout);

        using var httpSpan = StarlingTelemetry.Span("net", "http");
        Activity.Current?.SetTag("http.method", request.Method);
        Activity.Current?.SetTag("http.url", url.ToString());
        Activity.Current?.SetTag("server.address", url.Host);
        Activity.Current?.SetTag("server.port", port);
        StarlingTelemetry.Counter("net.http.requests", 1);

        using var message = BuildRequestMessage(request);
        if (!request.Body.IsEmpty)
        {
            StarlingTelemetry.Counter("net.http.bytes_out", request.Body.Length);
        }

        try
        {
            using var response = await _http.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, requestCts.Token).ConfigureAwait(false);

            var body = await response.Content.ReadAsByteArrayAsync(requestCts.Token).ConfigureAwait(false);
            var result = BuildResponse(response, url, port, body);

            StoreResponseCookies(result, url);
            Activity.Current?.SetTag("http.status_code", result.StatusCode);
            Activity.Current?.SetTag("network.protocol.version", response.Version.ToString());
            Activity.Current?.SetTag("http.response.body.size", body.Length);
            StarlingTelemetry.Counter("net.http.bytes_in", body.Length);
            if (result.StatusCode >= 500)
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, $"HTTP {result.StatusCode}");
            }
            return Result<HttpResponse, NetworkError>.Ok(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            RecordError(NetworkError.RequestTimeout);
            return Result<HttpResponse, NetworkError>.Err(NetworkError.RequestTimeout);
        }
        catch (HttpRequestException ex)
        {
            var error = MapException(ex);
            RecordError(error);
            return Result<HttpResponse, NetworkError>.Err(error);
        }
    }

    private HttpRequestMessage BuildRequestMessage(HttpRequest request)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url.ToString())
        {
            // ALPN chooses the wire protocol; fall back to HTTP/1.1 if the peer
            // does not offer HTTP/2.
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        if (!request.Body.IsEmpty)
        {
            message.Content = new ReadOnlyMemoryContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            // Content-* headers belong on the content object; everything else on
            // the request. TryAddWithoutValidation keeps values verbatim.
            if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return message;
    }

    private HttpResponse BuildResponse(HttpResponseMessage message, StarlingUrl url, int port, byte[] body)
    {
        var headers = new HttpHeaders();
        // NonValidated preserves raw values and duplicates (notably repeated
        // Set-Cookie), which the fetch/XHR bindings depend on.
        foreach (var header in message.Headers.NonValidated)
        {
            foreach (var value in header.Value)
            {
                headers.Add(header.Key, value);
            }
        }
        foreach (var header in message.Content.Headers.NonValidated)
        {
            foreach (var value in header.Value)
            {
                headers.Add(header.Key, value);
            }
        }

        var protocol = VersionString(message.Version);
        var response = new HttpResponse(
            protocol,
            (int)message.StatusCode,
            message.ReasonPhrase ?? string.Empty,
            headers,
            body)
        {
            Security = new ConnectionSecurity(
                protocol,
                url.IsHttps,
                url.IsHttps ? _connector.CertificateFor(url.Host!, port) : null),
        };
        return response;
    }

    private static string VersionString(Version version) => version switch
    {
        { Major: 1, Minor: 0 } => "HTTP/1.0",
        { Major: 1 } => "HTTP/1.1",
        { Major: 2 } => "HTTP/2",
        { Major: 3 } => "HTTP/3",
        _ => $"HTTP/{version.Major}.{version.Minor}",
    };

    private void RecordError(NetworkError error)
    {
        var message = error.ToString();
        StarlingTelemetry.Counter("net.http.failures", 1);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, message);
        StarlingHttpClientLog.RequestFailed(_log, message);
    }

    private static NetworkError MapException(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => NetworkError.DnsFailure,
        HttpRequestError.ConnectionError => NetworkError.ConnectFailed,
        HttpRequestError.SecureConnectionError =>
            ex.InnerException is System.Security.Authentication.AuthenticationException
                ? NetworkError.TlsCertificateRejected
                : NetworkError.TlsHandshakeFailed,
        HttpRequestError.HttpProtocolError => NetworkError.ProtocolError,
        HttpRequestError.VersionNegotiationError => NetworkError.ProtocolError,
        _ => NetworkError.TransportFailure,
    };

    /// <summary>
    /// Inject the cookie jar's view of the world into the request, but only when
    /// the caller hasn't already supplied a Cookie header.
    /// </summary>
    private void ApplyRequestCookies(HttpRequest request, StarlingUrl url)
    {
        if (_options.CookieJar is { } jar && !request.Headers.Contains("Cookie"))
        {
            var cookieHeader = jar.BuildCookieHeader(url);
            if (cookieHeader.Length > 0)
            {
                request.Headers.Set("Cookie", cookieHeader);
            }
        }
    }

    /// <summary>Persist any Set-Cookie headers from the response into the jar.</summary>
    private void StoreResponseCookies(HttpResponse response, StarlingUrl url)
    {
        if (_options.CookieJar is { } jar)
        {
            var setCookies = response.Headers.GetAll("Set-Cookie");
            if (setCookies.Count > 0)
            {
                jar.StoreFromHeaders(url, setCookies);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _http.Dispose();
    }
}

public sealed class StarlingHttpClientOptions
{
    /// <summary>
    /// Browser-identifying User-Agent presented on every request. Many sites
    /// (notably google.com) sniff this and serve a degraded, JS-free page to any
    /// UA they don't recognise as a modern browser, so we present as a current
    /// Chrome on macOS. Injected by <see cref="StarlingHttpClient.SendAsync"/>
    /// when the request doesn't already carry a User-Agent.
    /// </summary>
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 "
        + "(KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36";

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public string UserAgent { get; init; } = DefaultUserAgent;
    public IReadOnlyList<string> AlpnProtocols { get; init; } = ["h2", "http/1.1"];
    public CookieJar? CookieJar { get; init; }

    /// <summary>
    /// Optional logger factory. When set, the client emits per-request log
    /// messages. Tracing spans and metrics are always emitted via
    /// <see cref="Starling.Common.Diagnostics.StarlingTelemetry"/> regardless.
    /// Defaults to <see cref="NullLoggerFactory.Instance"/>.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; init; }
}

public enum NetworkError
{
    BadUrl,
    UnsupportedScheme,
    DnsFailure,
    ConnectFailed,
    ConnectTimeout,
    RequestTimeout,
    TlsHandshakeFailed,
    TlsCertificateRejected,
    TransportFailure,
    ProtocolError,
}
