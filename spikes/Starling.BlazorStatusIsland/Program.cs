// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Starling.BlazorStatusIsland;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

await builder.Build().RunAsync();
