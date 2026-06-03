// SPDX-License-Identifier: Apache-2.0
namespace Starling.Html;

/// <summary>
/// Settable holder for the active <see cref="IHtmlParserBackend"/>. Defaults to
/// the Starling parser, so behavior is unchanged until something assigns a
/// different backend at startup.
/// </summary>
/// <remarks>
/// A holder (rather than the lazy selector the JS seam uses) because fragment
/// parsing is called from <c>Starling.Bindings</c>, which references
/// <c>Starling.Html</c> but not <c>Starling.Engine</c>. A selector living in the
/// engine could not reach those call sites, so every site reads this one holder,
/// assigned once by the engine at startup. AngleSharp never enters
/// <c>Starling.Html</c>: it lives only in the backend project, which the engine
/// references and uses to set <see cref="Backend"/>.
/// </remarks>
public static class HtmlParsing
{
    /// <summary>The active backend. Assigned once at startup; the default is the
    /// Starling parser.</summary>
    public static IHtmlParserBackend Backend { get; set; } = new StarlingHtmlBackend();
}
