// SPDX-License-Identifier: Apache-2.0
using Starling.Common.Diagnostics;
using Starling.Dom;

namespace Starling.Html;

/// <summary>
/// The active HTML-parser backend behind <see cref="HtmlParsing.Backend"/>.
/// Mirrors the JS-engine seam (<c>IScriptEngineFactory</c>): one interface, a
/// default Starling implementation, and an opt-in alternative selected at
/// startup. Both <see cref="Parse"/> (full document) and
/// <see cref="ParseFragment"/> (<c>innerHTML</c> and friends) route through here
/// so a backend swap covers every parse path, not just document load.
/// </summary>
public interface IHtmlParserBackend
{
    /// <summary>Backend identifier, e.g. <c>"starling"</c> or <c>"anglesharp"</c>.</summary>
    string Name { get; }

    /// <summary>Parses a full document. See <see cref="HtmlParser.Parse"/> for the
    /// <paramref name="scriptingEnabled"/> contract.</summary>
    Document Parse(string html, IDiagnostics? diagnostics, bool scriptingEnabled);

    /// <summary>Runs the HTML fragment parsing algorithm (§13.4) for
    /// <paramref name="markup"/> in the context of <paramref name="context"/>,
    /// returning a <see cref="DocumentFragment"/> owned by
    /// <paramref name="ownerDocument"/>.</summary>
    DocumentFragment ParseFragment(string markup, Element context,
        Document ownerDocument, IDiagnostics? diagnostics);
}
