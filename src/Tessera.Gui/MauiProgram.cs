using Microsoft.Extensions.DependencyInjection;
using Tessera.Telemetry;

namespace Tessera.Gui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        // Logs/traces/metrics flow to the OTLP endpoint Aspire passes in
        // via OTEL_EXPORTER_OTLP_ENDPOINT when this app is launched as an
        // Aspire resource. Running directly (without Aspire) silently
        // drops the exports — same wiring, no-op exporter.
        builder.AddTesseraTelemetry("tessera-gui");

        // MainPage resolves IDiagnostics from DI; register it as transient so
        // App's CreateWindow can pull a fresh instance each time.
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
