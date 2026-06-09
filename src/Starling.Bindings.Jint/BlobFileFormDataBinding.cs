namespace Starling.Bindings.Jint;

/// <summary>
/// Real <c>Blob</c> / <c>File</c> / <c>FormData</c> classes for the Jint backend,
/// mirroring the canonical backend's behavior. Implemented as self-contained JS
/// classes (in-memory byte containers + an entry list) so they need no native
/// state table: a Blob stores its bytes as a Uint8Array; File extends Blob with
/// <c>name</c>/<c>lastModified</c>; FormData is a full entry list
/// (append/set/delete/get/getAll/has/forEach/keys/values/iterator) accepting
/// string and Blob/File values.
/// </summary>
/// <remarks>
/// Installed after NodeBindings (so the <c>__starlingFormDataEntries</c> hook used
/// by <c>new FormData(formElement)</c> exists) and before/around FetchBinding,
/// whose <c>Response.blob()</c> now mints a real <c>Blob</c>. The non-enumerable
/// <c>__bytes()</c> method exposes a Blob's bytes to the fetch body path.
/// </remarks>
internal static class BlobFileFormDataBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ctx.Engine.Execute(Bootstrap, "<blob-file-formdata>");
    }

    private const string Bootstrap = """
    (function () {
      'use strict';
      const BYTES = Symbol('bytes');
      const enc = new TextEncoder();

      function partsToBytes(parts) {
        if (parts === undefined || parts === null) return new Uint8Array(0);
        if (!Array.isArray(parts) && typeof parts[Symbol.iterator] !== 'function')
          throw new TypeError("Blob parts must be an iterable");
        const chunks = []; let total = 0;
        for (const p of parts) {
          let b;
          if (p instanceof Blob) b = p.__bytes();
          else if (p instanceof ArrayBuffer) b = new Uint8Array(p.slice(0));
          else if (ArrayBuffer.isView(p)) b = new Uint8Array(p.buffer.slice(p.byteOffset, p.byteOffset + p.byteLength));
          else b = enc.encode(typeof p === 'string' ? p : String(p));
          chunks.push(b); total += b.length;
        }
        const out = new Uint8Array(total); let o = 0;
        for (const c of chunks) { out.set(c, o); o += c.length; }
        return out;
      }

      class Blob {
        constructor(parts, options) {
          this[BYTES] = partsToBytes(parts);
          this._type = (options && options.type !== undefined) ? String(options.type).toLowerCase() : '';
        }
        get size() { return this[BYTES].length; }
        get type() { return this._type; }
        // Non-spec helper for the fetch body path + slicing.
        __bytes() { return this[BYTES]; }
        arrayBuffer() {
          const b = this[BYTES];
          return Promise.resolve(b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength));
        }
        text() { return Promise.resolve(new TextDecoder().decode(this[BYTES])); }
        slice(start, end, contentType) {
          const b = this[BYTES], n = b.length;
          let s = start === undefined ? 0 : (start < 0 ? Math.max(n + (start | 0), 0) : Math.min(start | 0, n));
          let e = end === undefined ? n : (end < 0 ? Math.max(n + (end | 0), 0) : Math.min(end | 0, n));
          const out = new Blob([], { type: contentType !== undefined ? String(contentType) : '' });
          out[BYTES] = b.slice(s, Math.max(s, e));
          return out;
        }
        get [Symbol.toStringTag]() { return 'Blob'; }
      }

      class File extends Blob {
        constructor(parts, name, options) {
          super(parts, options);
          if (name === undefined) throw new TypeError("File requires a name");
          this._name = String(name);
          this._lastModified = (options && options.lastModified !== undefined) ? Number(options.lastModified) : 0;
        }
        get name() { return this._name; }
        get lastModified() { return this._lastModified; }
        get [Symbol.toStringTag]() { return 'File'; }
      }

      function fdValue(value, filename) {
        if (value instanceof Blob) {
          if (!(value instanceof File))
            return new File([value], filename !== undefined ? String(filename) : 'blob', { type: value.type });
          if (filename !== undefined)
            return new File([value], String(filename), { type: value.type, lastModified: value.lastModified });
          return value;
        }
        return String(value);
      }

      class FormData {
        constructor(form) {
          this.__entries = [];
          if (form !== undefined && form !== null && typeof __starlingFormDataEntries === 'function') {
            const items = __starlingFormDataEntries(form);
            for (let i = 0; i < items.length; i++) this.__entries.push([String(items[i][0]), String(items[i][1])]);
          }
        }
        append(name, value, filename) { this.__entries.push([String(name), fdValue(value, filename)]); }
        set(name, value, filename) {
          name = String(name); const v = fdValue(value, filename);
          const out = []; let placed = false;
          for (const e of this.__entries) {
            if (e[0] === name) { if (!placed) { out.push([name, v]); placed = true; } }
            else out.push(e);
          }
          if (!placed) out.push([name, v]);
          this.__entries = out;
        }
        delete(name) { name = String(name); this.__entries = this.__entries.filter(e => e[0] !== name); }
        get(name) { name = String(name); for (const e of this.__entries) if (e[0] === name) return e[1]; return null; }
        getAll(name) { name = String(name); return this.__entries.filter(e => e[0] === name).map(e => e[1]); }
        has(name) { name = String(name); return this.__entries.some(e => e[0] === name); }
        forEach(cb, thisArg) { for (const e of this.__entries.slice()) cb.call(thisArg, e[1], e[0], this); }
        *entries() { for (const e of this.__entries.slice()) yield [e[0], e[1]]; }
        *keys() { for (const e of this.__entries.slice()) yield e[0]; }
        *values() { for (const e of this.__entries.slice()) yield e[1]; }
        [Symbol.iterator]() { return this.entries(); }
        get [Symbol.toStringTag]() { return 'FormData'; }
      }

      const g = globalThis;
      const def = (n, v) => Object.defineProperty(g, n, { value: v, writable: true, enumerable: false, configurable: true });
      def('Blob', Blob);
      def('File', File);
      def('FormData', FormData);
    })();
    """;
}
