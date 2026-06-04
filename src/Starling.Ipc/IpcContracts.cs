// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace Starling.Ipc;

public static class IpcProtocol
{
    public const int Version = 1;
    public const int DefaultMaxMessageBytes = 16 * 1024 * 1024;
}

public static class IpcMessageKind
{
    public const string Error = "error";
    public const string Hello = "hello";
    public const string HelloAck = "helloAck";
    public const string CreatePage = "createPage";
    public const string PageCreated = "pageCreated";
    public const string Navigate = "navigate";
    public const string NavigateResult = "navigateResult";
    public const string SetViewport = "setViewport";
    public const string ViewportSet = "viewportSet";
    public const string PointerClick = "pointerClick";
    public const string PointerClickResult = "pointerClickResult";
    public const string PumpFrame = "pumpFrame";
    public const string PumpFrameResult = "pumpFrameResult";
    public const string FrameReady = "frameReady";
    public const string GetSnapshot = "getSnapshot";
    public const string PageSnapshot = "pageSnapshot";
    public const string ClosePage = "closePage";
    public const string PageClosed = "pageClosed";
    public const string Shutdown = "shutdown";
    public const string ShutdownAck = "shutdownAck";
}

public sealed record IpcEnvelope(
    int ProtocolVersion,
    long MessageId,
    string? SessionId,
    string Kind,
    JsonElement Payload);

public sealed record IpcError(string Code, string Message, string? Detail = null);

public sealed record HelloRequest(
    string ClientName,
    int MinProtocolVersion = IpcProtocol.Version,
    int MaxProtocolVersion = IpcProtocol.Version);

public sealed record HelloAck(
    string ServerName,
    int ProtocolVersion,
    string[] Capabilities);

public sealed record CreatePageRequest(
    int Width,
    int Height,
    float FontSize = 16f,
    string? SessionId = null);

public sealed record PageCreated(string SessionId);

public sealed record NavigateRequest(
    string Url,
    int Width,
    int Height,
    float FontSize = 16f,
    int TimeoutMs = 30000);

public sealed record NavigateResult(
    bool Ok,
    string? Url,
    string? Title,
    string? Error,
    int LayoutVersion);

public sealed record SetViewportRequest(
    int Width,
    int Height,
    float FontSize = 16f);

public sealed record ViewportSetResult(
    bool Ok,
    string? Error,
    int LayoutVersion);

public sealed record PointerClickRequest(
    double X,
    double Y,
    int Button = 0);

public sealed record PointerClickResult(
    bool Hit,
    bool Mutated,
    int LayoutVersion);

public sealed record PumpFrameRequest(long ElapsedMs);

public sealed record PumpFrameResult(
    bool Mutated,
    int LayoutVersion);

public sealed record DirtyRect(double X, double Y, double Width, double Height);

public sealed record BlobRef(
    string Encoding,
    string ContentType,
    string Data);

public sealed record FrameReady(
    long FrameId,
    int LayoutVersion,
    int Width,
    int Height,
    float DeviceScaleFactor,
    DirtyRect[] DirtyRects,
    BlobRef Blob);

public sealed record GetSnapshotRequest(string[] Selectors);

public sealed record PageSnapshot(
    string? Url,
    string? Title,
    int LayoutVersion,
    ElementSnapshot[] Elements);

public sealed record ElementSnapshot(
    string Selector,
    int Index,
    string TagName,
    string? Id,
    string? ClassName,
    string TextContent,
    Dictionary<string, string?> Attributes);

public sealed record ClosePageRequest();

public sealed record PageClosed(string SessionId);

public sealed record ShutdownRequest();

public sealed record ShutdownAck();
