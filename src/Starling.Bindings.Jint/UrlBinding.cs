// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Runtime;

namespace Starling.Bindings.Jint;

/// <summary>
/// The WHATWG <c>URL</c> and <c>URLSearchParams</c> constructors on the Jint
/// backend. Mirrors the surface in
/// <c>Starling.Bindings/CoreWebApiBinding.cs</c> (the Starling-engine impl),
/// which is itself backed by <see cref="System.Uri"/> / <see cref="System.UriBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Angular's runtime constructs <c>new URL(...)</c> during boot. Without the
/// global the main bundle throws <c>ReferenceError: URL is not defined</c> and
/// the app never hydrates.
/// </para>
/// <para>
/// The C# bridge does the parsing (so URL semantics live in one place) and the
/// JS bootstrap defines the Web-IDL classes. <c>URLSearchParams</c> is a small
/// pure-JS multimap; <c>url.searchParams</c> stays in sync because every
/// mutation re-parses through the bridge.
/// </para>
/// </remarks>
internal static class UrlBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;

        var bridge = new JsObject(engine);

        void Fn(string name, Func<JsValue, JsValue[], JsValue> body, int len) =>
            JintInterop.DefineMethod(engine, bridge, name, body, len);

        // parse(input, base?) -> { href, origin, protocol, host, hostname,
        // port, pathname, search, hash } or throws a TypeError.
        Fn("parse", (_, a) =>
        {
            var input = Str(a, 0);
            var hasBase = a.Length > 1 && !a[1].IsUndefined() && !a[1].IsNull();
            var baseUrl = hasBase ? a[1].ToString() : null;
            var uri = Parse(engine, input, baseUrl);
            return Components(engine, uri);
        }, 2);

        // reparse(href, scheme|null, host|null, ...) — apply one component change
        // by rebuilding from the current href, then return the new components.
        // The single entry point keeps URL-mutation semantics in C#.
        Fn("withComponent", (_, a) =>
        {
            var href = Str(a, 0);
            var which = Str(a, 1);
            var value = Str(a, 2);
            var uri = Parse(engine, href, null);
            var builder = new UriBuilder(uri);
            switch (which)
            {
                case "protocol": builder.Scheme = value.TrimEnd(':'); break;
                case "hostname": builder.Host = value; break;
                case "host":
                    var hp = value.Split(':', 2);
                    builder.Host = hp[0];
                    if (hp.Length > 1 && int.TryParse(hp[1], NumberStyles.None, CultureInfo.InvariantCulture, out var hpp))
                        builder.Port = hpp;
                    break;
                case "port":
                    if (value.Length == 0) builder.Port = -1;
                    else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 65535)
                        builder.Port = p;
                    break;
                case "pathname": builder.Path = value.Length == 0 || value[0] == '/' ? value : "/" + value; break;
                case "search": builder.Query = value.TrimStart('?'); break;
                case "hash": builder.Fragment = value.TrimStart('#'); break;
            }
            return Components(engine, builder.Uri);
        }, 3);

        JintInterop.DefineDataProp(engine.Global, "__surl", bridge,
            writable: false, enumerable: false, configurable: true);

        engine.Execute(Bootstrap, "<url-bootstrap>");
        engine.Execute("delete globalThis.__surl;", "<url-bootstrap-cleanup>");
    }

    private static string Str(JsValue[] a, int i) =>
        i < a.Length && !a[i].IsUndefined() && !a[i].IsNull() ? a[i].ToString() : "";

    private static Uri Parse(Engine engine, string input, string? baseUrl)
    {
        if (baseUrl is null)
        {
            if (Uri.TryCreate(input, UriKind.Absolute, out var abs)) return abs;
        }
        else if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) && Uri.TryCreate(b, input, out var rel))
        {
            return rel;
        }
        throw new JavaScriptException(engine.Intrinsics.TypeError, "Invalid URL");
    }

    private static JsObject Components(Engine engine, Uri uri)
    {
        var port = uri.IsDefaultPort ? "" : uri.Port.ToString(CultureInfo.InvariantCulture);
        var host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
        var origin = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port.ToString(CultureInfo.InvariantCulture)}";

        var o = new JsObject(engine);
        void Set(string k, string v) =>
            JintInterop.DefineDataProp(o, k, new JsString(v), writable: true, enumerable: true, configurable: true);
        Set("href", uri.AbsoluteUri);
        Set("origin", origin);
        Set("protocol", uri.Scheme + ":");
        Set("host", host);
        Set("hostname", uri.Host);
        Set("port", port);
        Set("pathname", uri.AbsolutePath);
        Set("search", uri.Query);
        Set("hash", uri.Fragment);
        return o;
    }

    // §6 (URLSearchParams) + §4 (URL) — Web-IDL classes over __surl.
    private const string Bootstrap = """
    (function (B) {
      'use strict';

      function parseQuery(init) {
        const list = [];
        if (init === undefined || init === null || init === '') return list;
        let s = String(init);
        if (s[0] === '?') s = s.slice(1);
        if (s === '') return list;
        for (const pair of s.split('&')) {
          if (pair === '') continue;
          const eq = pair.indexOf('=');
          const k = eq < 0 ? pair : pair.slice(0, eq);
          const v = eq < 0 ? '' : pair.slice(eq + 1);
          list.push([decodeURIComponent(k.replace(/\+/g, ' ')), decodeURIComponent(v.replace(/\+/g, ' '))]);
        }
        return list;
      }

      const enc = s => encodeURIComponent(s).replace(/%20/g, '+');

      class URLSearchParams {
        #list;
        #onchange;
        constructor(init) {
          if (init && typeof init === 'object' && typeof init[Symbol.iterator] === 'function') {
            this.#list = [];
            for (const e of init) this.#list.push([String(e[0]), String(e[1])]);
          } else if (init && typeof init === 'object') {
            this.#list = Object.keys(init).map(k => [k, String(init[k])]);
          } else {
            this.#list = parseQuery(init);
          }
          this.#onchange = null;
        }
        // Internal: URL wires a callback so query edits flow back to the URL.
        __bind(cb, list) { this.#onchange = cb; if (list) this.#list = list; }
        #notify() { if (this.#onchange) this.#onchange(this.toString()); }
        append(n, v) { this.#list.push([String(n), String(v)]); this.#notify(); }
        delete(n) { n = String(n); this.#list = this.#list.filter(e => e[0] !== n); this.#notify(); }
        get(n) { n = String(n); for (const e of this.#list) if (e[0] === n) return e[1]; return null; }
        getAll(n) { n = String(n); return this.#list.filter(e => e[0] === n).map(e => e[1]); }
        has(n) { n = String(n); return this.#list.some(e => e[0] === n); }
        set(n, v) {
          n = String(n); v = String(v);
          let done = false;
          this.#list = this.#list.filter(e => {
            if (e[0] !== n) return true;
            if (!done) { e[1] = v; done = true; return true; }
            return false;
          });
          if (!done) this.#list.push([n, v]);
          this.#notify();
        }
        sort() { this.#list.sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0); this.#notify(); }
        forEach(cb, thisArg) { for (const e of this.#list) cb.call(thisArg, e[1], e[0], this); }
        *entries() { for (const e of this.#list) yield [e[0], e[1]]; }
        *keys() { for (const e of this.#list) yield e[0]; }
        *values() { for (const e of this.#list) yield e[1]; }
        [Symbol.iterator]() { return this.entries(); }
        get size() { return this.#list.length; }
        toString() { return this.#list.map(e => enc(e[0]) + '=' + enc(e[1])).join('&'); }
      }

      class URL {
        #c;
        #sp;
        constructor(input, base) {
          if (input === undefined) throw new TypeError('URL requires an input');
          this.#c = B.parse(String(input), base);
          this.#wireSearchParams();
        }
        #wireSearchParams() {
          this.#sp = new URLSearchParams(this.#c.search);
          const self = this;
          this.#sp.__bind(function (q) {
            self.#c = B.withComponent(self.#c.href, 'search', q.length ? '?' + q : '');
          });
        }
        get href() { return this.#c.href; }
        set href(v) { this.#c = B.parse(String(v), undefined); this.#wireSearchParams(); }
        get origin() { return this.#c.origin; }
        get protocol() { return this.#c.protocol; }
        set protocol(v) { this.#c = B.withComponent(this.#c.href, 'protocol', String(v)); }
        get host() { return this.#c.host; }
        set host(v) { this.#c = B.withComponent(this.#c.href, 'host', String(v)); }
        get hostname() { return this.#c.hostname; }
        set hostname(v) { this.#c = B.withComponent(this.#c.href, 'hostname', String(v)); }
        get port() { return this.#c.port; }
        set port(v) { this.#c = B.withComponent(this.#c.href, 'port', String(v)); }
        get pathname() { return this.#c.pathname; }
        set pathname(v) { this.#c = B.withComponent(this.#c.href, 'pathname', String(v)); }
        get search() { return this.#c.search; }
        set search(v) { this.#c = B.withComponent(this.#c.href, 'search', String(v)); this.#wireSearchParams(); }
        get hash() { return this.#c.hash; }
        set hash(v) { this.#c = B.withComponent(this.#c.href, 'hash', String(v)); }
        get searchParams() { return this.#sp; }
        toString() { return this.#c.href; }
        toJSON() { return this.#c.href; }
      }

      const g = globalThis;
      const def = (name, value) => Object.defineProperty(g, name, {
        value, writable: true, enumerable: false, configurable: true
      });
      def('URL', URL);
      def('URLSearchParams', URLSearchParams);
    })(globalThis.__surl);
    """;
}
