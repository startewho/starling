// SPDX-License-Identifier: Apache-2.0

namespace Starling.Common.Diagnostics;

public static class DiagnosticScope
{
    public static IDisposable Noop { get; } = new NoopDisposable();

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
