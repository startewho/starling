// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Starling.Ipc;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(IpcEnvelope))]
[JsonSerializable(typeof(IpcError))]
[JsonSerializable(typeof(HelloRequest))]
[JsonSerializable(typeof(HelloAck))]
[JsonSerializable(typeof(CreatePageRequest))]
[JsonSerializable(typeof(PageCreated))]
[JsonSerializable(typeof(NavigateRequest))]
[JsonSerializable(typeof(NavigateResult))]
[JsonSerializable(typeof(SetViewportRequest))]
[JsonSerializable(typeof(ViewportSetResult))]
[JsonSerializable(typeof(PointerClickRequest))]
[JsonSerializable(typeof(PointerClickResult))]
[JsonSerializable(typeof(PumpFrameRequest))]
[JsonSerializable(typeof(PumpFrameResult))]
[JsonSerializable(typeof(DirtyRect))]
[JsonSerializable(typeof(BlobRef))]
[JsonSerializable(typeof(FrameReady))]
[JsonSerializable(typeof(GetSnapshotRequest))]
[JsonSerializable(typeof(PageSnapshot))]
[JsonSerializable(typeof(ElementSnapshot))]
[JsonSerializable(typeof(ClosePageRequest))]
[JsonSerializable(typeof(PageClosed))]
[JsonSerializable(typeof(ShutdownRequest))]
[JsonSerializable(typeof(ShutdownAck))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
public partial class IpcJsonContext : JsonSerializerContext;

public static class IpcJson
{
    public static JsonSerializerOptions Options { get; } = new(IpcJsonContext.Default.Options);
}
