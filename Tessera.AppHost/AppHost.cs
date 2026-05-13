// Aspire AppHost: brings up the Tessera dashboard at http://localhost:18888
// (or the port Aspire chose) and launches the registered resources. Run with:
//
//     dotnet run --project Tessera.AppHost
//
// The dashboard surfaces stdout/stderr, environment, and (where wired) OTel
// traces + metrics from each resource.

var builder = DistributedApplication.CreateBuilder(args);

// The MAUI desktop GUI. Aspire launches it via `dotnet run --framework
// net10.0-maccatalyst`, which boots the Mac Catalyst window. The resource
// stays "running" until the user closes the window.
builder.AddProject<Projects.Tessera_Gui>("gui");

// The headless CLI: a transient render-then-exit process. Pre-baked args
// render the bundled hello.html fixture into out.png; tweak via the Aspire
// dashboard's resource panel or by editing the WithArgs call below. The
// WorkingDirectory is the repo root so `testdata/` resolves.
builder.AddProject<Projects.Tessera_Headless>("headless")
    .WithArgs("render", "testdata/hello.html", "-o", "out.png")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development");

builder.Build().Run();
