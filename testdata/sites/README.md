# testdata/sites/

Local, build-free test sites served at stable `localhost` paths so the Starling
engine (and a real browser) can load them over plain HTTP. This directory also
holds captured full-page HTML fixtures (e.g. `google-home.html`); those are
reachable at their file path too once the server is up.

## Convention

Each subdirectory is one site, served at a path matching its folder name:

```
testdata/sites/
  index.html        ->  http://localhost:8088/
  todo/index.html   ->  http://localhost:8088/todo/
  words/index.html  ->  http://localhost:8088/words/
```

Drop a new folder with an `index.html` in it and it's available at
`http://localhost:8088/<folder>/` after the next AppHost (re)start — no code
changes, no rebuild.

## How it's served

The Aspire AppHost adds a YARP resource with static-file serving pointed at this
directory (see `src/Starling.AppHost/AppHost.cs`, the `"sites"` resource). The YARP
integration runs as a container and **copies this folder into the container when
it starts** — it is not a live bind-mount. So adding a site, or editing existing
HTML/CSS/JS, requires restarting the AppHost (or just the `sites` resource from
the Aspire dashboard) for the change to be served.

Run it with:

```bash
dotnet run --project src/Starling.AppHost
# or: aspire run
```

Then open the `sites` endpoint from the Aspire dashboard, or hit the fixed host
port directly (`http://localhost:8088/`).
