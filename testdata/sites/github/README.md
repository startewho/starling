# github.com homepage snapshot (offline test fixture)

Logged-out github.com homepage captured for offline engine tests, so parser /
compiler / layout work can run against the real bundles without the network or
the live browser.

- `index.html` — the homepage HTML. Absolute `github.githubassets.com/assets/`
  URLs were rewritten to local `assets/` so the snapshot is self-contained.
- `assets/*.js` — 73 script bundles (webpack chunks, react-core, react-lib,
  behaviors, primer-react, landing-pages, …).
- `assets/*.css` — 27 stylesheets.

Served by the AppHost `sites` resource at `http://localhost:8088/github/`.
Images / fonts / avatars on other CDNs are not mirrored (they 404 offline,
which does not affect JS/CSS parsing tests).

Snapshot is a point-in-time copy; github changes asset hashes on each deploy.
