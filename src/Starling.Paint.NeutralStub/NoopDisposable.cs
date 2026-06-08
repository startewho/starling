// SPDX-License-Identifier: Apache-2.0
namespace Starling.Paint.NeutralStub;

internal sealed class NoopDisposable : IDisposable
{
    public static readonly NoopDisposable Instance = new();
    public void Dispose() { }
}
