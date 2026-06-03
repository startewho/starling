// SPDX-License-Identifier: Apache-2.0
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;

namespace Starling.Bindings.Jint;

/// <summary>
/// The WHATWG Encoding API (<c>TextEncoder</c> / <c>TextDecoder</c>) on the Jint
/// backend. Mirrors the surface in
/// <c>Starling.Bindings/CoreWebApiBinding.cs</c> (the Starling-engine impl).
/// </summary>
/// <remarks>
/// <para>
/// Angular's runtime (and many other bundles) call <c>new TextDecoder()</c> /
/// <c>new TextEncoder()</c> during module evaluation. Without these globals the
/// main bundle throws <c>ReferenceError: TextDecoder is not defined</c> and the
/// app never boots. The Jint <c>FetchBinding</c> bootstrap also assumes
/// <c>TextDecoder</c> exists (its <c>Response.blob().text()</c> uses it).
/// </para>
/// <para>
/// We only implement UTF-8, the one label web code relies on in practice (and
/// the default). A non-UTF-8 label is accepted and treated as UTF-8 rather than
/// throwing, so feature checks pass. <c>encodeInto</c> is provided too because
/// some bundles probe for it.
/// </para>
/// <para>
/// The C# bridge does the byte work; a small JS bootstrap defines the Web-IDL
/// classes so <c>encode()</c> returns a real <c>Uint8Array</c> (built from the
/// bridge's <c>ArrayBuffer</c>). The private bridge is deleted once the classes
/// capture it.
/// </para>
/// </remarks>
internal static class EncodingBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;

        var bridge = new JsObject(engine);

        void Fn(string name, Func<JsValue, JsValue[], JsValue> body, int len) =>
            JintInterop.DefineMethod(engine, bridge, name, body, len);

        // encode(str) -> ArrayBuffer of UTF-8 bytes. The JS class wraps it in a
        // Uint8Array so the return value is spec-shaped.
        Fn("encode", (_, a) =>
        {
            var s = a.Length > 0 && !a[0].IsUndefined() && !a[0].IsNull() ? a[0].ToString() : "";
            return engine.Intrinsics.ArrayBuffer.Construct(Encoding.UTF8.GetBytes(s));
        }, 1);

        // decode(bufferSource, ignoreBOM) -> string. Reads bytes from an
        // ArrayBuffer or any typed-array/DataView view.
        Fn("decode", (_, a) =>
        {
            var bytes = ExtractBytes(a.Length > 0 ? a[0] : JsValue.Undefined);
            var ignoreBom = a.Length > 1 && TypeConverter.ToBoolean(a[1]);
            var text = Encoding.UTF8.GetString(bytes);
            // §10.2.1 — strip a leading BOM unless ignoreBOM was requested.
            if (!ignoreBom && text.Length > 0 && text[0] == '﻿')
                text = text[1..];
            return JintInterop.Str(text);
        }, 2);

        JintInterop.DefineDataProp(engine.Global, "__senc", bridge,
            writable: false, enumerable: false, configurable: true);

        engine.Execute(Bootstrap, "<encoding-bootstrap>");
        engine.Execute("delete globalThis.__senc;", "<encoding-bootstrap-cleanup>");
    }

    /// <summary>Read the raw bytes out of an ArrayBuffer or an ArrayBuffer view
    /// (typed array / DataView). Honors the view's byteOffset/byteLength so a
    /// subarray decodes correctly. Empty for undefined/null/unknown input.</summary>
    private static byte[] ExtractBytes(JsValue v)
    {
        if (v.IsUndefined() || v.IsNull()) return Array.Empty<byte>();
        if (v.IsArrayBuffer() && v.AsArrayBuffer() is { } ab) return ab;
        if (v is ObjectInstance oi)
        {
            var bufVal = oi.Get("buffer");
            if (bufVal.IsArrayBuffer() && bufVal.AsArrayBuffer() is { } backing)
            {
                var offset = ToInt(oi.Get("byteOffset"));
                var length = ToInt(oi.Get("byteLength"));
                if (offset == 0 && (length == 0 || length == backing.Length))
                    return backing;
                if (offset >= 0 && length >= 0 && offset + length <= backing.Length)
                {
                    var slice = new byte[length];
                    Array.Copy(backing, offset, slice, 0, length);
                    return slice;
                }
                return backing;
            }
        }
        return Array.Empty<byte>();

        static int ToInt(JsValue n) => n.IsNumber() ? (int)n.AsNumber() : 0;
    }

    // §10.1 / §10.2 — Web-IDL classes delegating to __senc.*.
    private const string Bootstrap = """
    (function (B) {
      'use strict';

      class TextEncoder {
        constructor() {}
        get encoding() { return 'utf-8'; }
        encode(input) {
          return new Uint8Array(B.encode(input === undefined ? '' : String(input)));
        }
        encodeInto(source, destination) {
          const bytes = new Uint8Array(B.encode(source === undefined ? '' : String(source)));
          const n = Math.min(bytes.length, destination.length);
          for (let i = 0; i < n; i++) destination[i] = bytes[i];
          // `read` is the number of UTF-16 code units consumed. We only report
          // an exact count when the whole input fit; otherwise approximate with
          // the source length, which is correct for the common all-fit path.
          return { read: n === bytes.length ? source.length : n, written: n };
        }
      }

      class TextDecoder {
        #fatal; #ignoreBOM; #encoding;
        constructor(label, options) {
          // Only UTF-8 is implemented; any label is treated as utf-8.
          this.#encoding = 'utf-8';
          this.#fatal = !!(options && options.fatal);
          this.#ignoreBOM = !!(options && options.ignoreBOM);
        }
        get encoding() { return this.#encoding; }
        get fatal() { return this.#fatal; }
        get ignoreBOM() { return this.#ignoreBOM; }
        decode(input, options) {
          return B.decode(input, this.#ignoreBOM);
        }
      }

      const g = globalThis;
      const def = (name, value) => Object.defineProperty(g, name, {
        value, writable: true, enumerable: false, configurable: true
      });
      def('TextEncoder', TextEncoder);
      def('TextDecoder', TextDecoder);
    })(globalThis.__senc);
    """;
}
