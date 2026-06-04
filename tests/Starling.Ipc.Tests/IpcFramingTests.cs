// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;

namespace Starling.Ipc.Tests;

[TestClass]
public sealed class IpcFramingTests
{
    [TestMethod]
    public async Task Length_prefixed_envelope_round_trips_typed_payload()
    {
        var stream = new MemoryStream();
        var written = IpcFraming.CreateEnvelope(
            42,
            "session-1",
            IpcMessageKind.Navigate,
            new NavigateRequest("http://127.0.0.1/blazor-status/", 360, 32));

        await IpcFraming.WriteAsync(stream, written);
        stream.Position = 0;

        var read = await IpcFraming.ReadAsync(stream);
        read.Should().NotBeNull();
        read!.MessageId.Should().Be(42);
        read.SessionId.Should().Be("session-1");
        read.Kind.Should().Be(IpcMessageKind.Navigate);

        var payload = read.ReadPayload<NavigateRequest>();
        payload.Url.Should().Be("http://127.0.0.1/blazor-status/");
        payload.Width.Should().Be(360);
        payload.Height.Should().Be(32);
        payload.FontSize.Should().Be(16f);
    }
}
