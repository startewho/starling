// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Starling.Paint;

namespace Starling.Engine;

/// <summary>
/// DI registration for the engine's shared services. Hosts that build a service
/// provider (the Avalonia GUI, future headless variants) call
/// <see cref="AddStarlingEngine"/> so the container constructs these — injecting
/// <c>ILoggerFactory</c>/<c>ILogger&lt;T&gt;</c> from the host's logging setup —
/// instead of <c>new</c>-ing them by hand.
///
/// <para>Only the genuinely shared, service-shaped types are registered.
/// Per-page/per-request objects (fetchers, layout, the HTTP client) are
/// parameterized by runtime data the container can't supply, so they stay
/// constructed by their owners. Every registered type also keeps a public
/// constructor, so tests and the headless CLI can still <c>new</c> them without
/// a container.</para>
/// </summary>
public static class StarlingEngineServiceCollectionExtensions
{
    public static IServiceCollection AddStarlingEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The paint façade is safe to share: its only mutable state is a
        // weak, document-keyed animation-timeline table, so one instance serves
        // every session without cross-talk.
        services.AddSingleton<Painter>();

        // A session and a one-shot engine are per-use units. Transient so each
        // resolve is independent; the container injects the logger factory and
        // the shared Painter, and the optional HTTP factory falls back to the
        // engine's default (shared-cookie) client.
        services.AddTransient<StarlingEngine>();
        services.AddTransient<BrowserSession>();

        return services;
    }
}
