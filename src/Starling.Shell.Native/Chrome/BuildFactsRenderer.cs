// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Starling.Shell.Native;

internal static class BuildFactsRenderer
{
    private static readonly Lazy<ServiceProvider> Services = new(CreateServices);

    public static string Render(string jsEngine, string renderBackend, string labelColor, string valueColor)
    {
        using var renderer = new HtmlRenderer(
            Services.Value,
            Services.Value.GetRequiredService<ILoggerFactory>());

        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            ["Commit"] = BuildLabel(),
            ["JsEngine"] = jsEngine,
            ["RenderBackend"] = renderBackend,
            ["LabelColor"] = labelColor,
            ["ValueColor"] = valueColor,
        });

        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<BuildFacts>(parameters);
            return root.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static string BuildLabel()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? string.Empty;

        var plus = info.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0) info = info[(plus + 1)..];
        return info.Length > 8 ? info[..8] : info;
    }
}
