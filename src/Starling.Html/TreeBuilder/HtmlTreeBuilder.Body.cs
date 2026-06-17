using System.Collections.Frozen;
using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    // §13.2.6.4.7 element category sets — build-once/read-many, so frozen.
    private static readonly FrozenSet<string> AddressBlockTags = new[]
    {
        "address", "article", "aside", "blockquote", "center", "details", "dialog",
        "dir", "div", "dl", "fieldset", "figcaption", "figure", "footer", "header",
        "hgroup", "main", "menu", "nav", "ol", "p", "search", "section", "summary", "ul",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> FormattingTags = new[]
    {
        "b", "big", "code", "em", "font", "i", "s", "small", "strike", "strong", "tt", "u",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static bool IsHeadingTag(string n) => n is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

    private void HandleInBody(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c:
                if (c.CodePoint == 0)
                {
                    return; // U+0000: parse error, ignore.
                }

                ReconstructActiveFormattingElements();
                InsertCharacter(CodePointToString(c.CodePoint));
                if (!IsWhitespaceChar(c.CodePoint))
                {
                    _framesetOk = false;
                }

                return;

            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;

            case StartTagToken { Name: "html" } start:
                if (_openElements.ContainsNamed("template"))
                {
                    return;
                }

                MergeAttributesInto(_openElements.Count > 0 ? _openElements[0] : null, start);
                return;

            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound"
                or "link" or "meta" or "noframes" or "script" or "style" or "template" or "title":
                HandleInHead(start);
                return;
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;

            case StartTagToken { Name: "body" } start:
                if (_openElements.Count < 2 || _openElements[1] is not { Namespace: HtmlNs, LocalName: "body" }
                    || _openElements.ContainsNamed("template"))
                {
                    return;
                }

                _framesetOk = false;
                MergeAttributesInto(_openElements[1], start);
                return;

            case StartTagToken { Name: "frameset" } start:
                if (_openElements.Count < 2 || _openElements[1] is not { Namespace: HtmlNs, LocalName: "body" })
                {
                    return;
                }

                if (!_framesetOk)
                {
                    return;
                }
                {
                    var body = _openElements[1];
                    body.ParentNode?.RemoveChild(body);
                    while (_openElements.Count > 1)
                    {
                        _openElements.Pop();
                    }

                    InsertHtmlElement(start);
                    _mode = InsertionMode.InFrameset;
                }
                return;

            case EndOfFileToken:
                if (_templateInsertionModes.Count > 0) { HandleInTemplate(token); return; }
                return; // stop parsing.

            case EndTagToken { Name: "body" }:
                if (!_openElements.HasInScope("body"))
                {
                    return;
                }

                _mode = InsertionMode.AfterBody;
                return;
            case EndTagToken { Name: "html" }:
                if (!_openElements.HasInScope("body"))
                {
                    return;
                }

                _mode = InsertionMode.AfterBody;
                HandleAfterBody(token);
                return;

            case StartTagToken start when AddressBlockTags.Contains(start.Name):
                ClosePIfInButtonScope();
                InsertHtmlElement(start);
                return;

            case StartTagToken start when IsHeadingTag(start.Name):
                ClosePIfInButtonScope();
                if (_openElements.Current is { Namespace: HtmlNs } cur && IsHeadingTag(cur.LocalName))
                {
                    _openElements.Pop();
                }

                InsertHtmlElement(start);
                return;

            case StartTagToken start when start.Name is "pre" or "listing":
                ClosePIfInButtonScope();
                InsertHtmlElement(start);
                _ignoreLf = true;
                _framesetOk = false;
                return;

            case StartTagToken start when start.Name == "form":
                if (_formElement is not null && !_openElements.ContainsNamed("template"))
                {
                    return;
                }

                ClosePIfInButtonScope();
                {
                    var form = InsertHtmlElement(start);
                    if (!_openElements.ContainsNamed("template"))
                    {
                        _formElement = form;
                    }
                }
                return;

            case StartTagToken start when start.Name == "li":
                StartListItem(start);
                return;
            case StartTagToken start when start.Name is "dd" or "dt":
                StartDefinitionItem(start);
                return;

            case StartTagToken start when start.Name == "plaintext":
                ClosePIfInButtonScope();
                InsertHtmlElement(start);
                _tokenizer.SetState(TokenizerState.Plaintext);
                return;

            case StartTagToken start when start.Name == "button":
                if (_openElements.HasInScope("button"))
                {
                    GenerateImpliedEndTags();
                    _openElements.PopUntilNamed("button");
                }
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                _framesetOk = false;
                return;

            // The end-tag block list (§13.2.6.4.7) is the start-tag block list
            // plus listing/pre. "p" is excluded — it is itself an implied-end-tag,
            // so it needs the dedicated </p> handler below (which exempts p) rather
            // than the generic GenerateImpliedEndTags()/PopUntilNamed path that
            // would drain the stack. button keeps its own case below.
            case EndTagToken end when end.Name != "p"
                && (AddressBlockTags.Contains(end.Name) || end.Name is "listing" or "pre"):
                if (!_openElements.HasInScope(end.Name))
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilNamed(end.Name);
                return;

            case EndTagToken { Name: "form" }:
                EndForm();
                return;

            case EndTagToken { Name: "p" }:
                if (!_openElements.HasInButtonScope("p"))
                {
                    HandleToken(Synthetic("p")); // re-dispatch: breaks out of foreign content.
                }

                ClosePElement();
                return;

            // §13.2.6.4.7 "</br>": parse error; act as a <br> start tag (attributes
            // dropped). Re-dispatched so it breaks out of any open foreign content.
            case EndTagToken { Name: "br" }:
                HandleToken(Synthetic("br"));
                return;

            case EndTagToken { Name: "li" }:
                if (!_openElements.HasInListItemScope("li"))
                {
                    return;
                }

                GenerateImpliedEndTags(except: "li");
                _openElements.PopUntilNamed("li");
                return;

            case EndTagToken end when end.Name is "dd" or "dt":
                if (!_openElements.HasInScope(end.Name))
                {
                    return;
                }

                GenerateImpliedEndTags(except: end.Name);
                _openElements.PopUntilNamed(end.Name);
                return;

            case EndTagToken end when IsHeadingTag(end.Name):
                if (!AnyHeadingInScope())
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilOneOf("h1", "h2", "h3", "h4", "h5", "h6");
                return;

            case StartTagToken { Name: "a" } start:
                {
                    var existing = _activeFormatting.LastBeforeMarker("a");
                    if (existing is not null)
                    {
                        RunAdoptionAgency("a");
                        _activeFormatting.Remove(existing);
                        if (_openElements.Contains(existing))
                        {
                            _openElements.Remove(existing);
                        }
                    }
                    ReconstructActiveFormattingElements();
                    var el = InsertHtmlElement(start);
                    _activeFormatting.Add(el);
                }
                return;

            case StartTagToken start when FormattingTags.Contains(start.Name):
                ReconstructActiveFormattingElements();
                {
                    var el = InsertHtmlElement(start);
                    _activeFormatting.Add(el);
                }
                return;

            case StartTagToken { Name: "nobr" } start:
                ReconstructActiveFormattingElements();
                if (_openElements.HasInScope("nobr"))
                {
                    RunAdoptionAgency("nobr");
                    ReconstructActiveFormattingElements();
                }
                {
                    var el = InsertHtmlElement(start);
                    _activeFormatting.Add(el);
                }
                return;

            case EndTagToken end when end.Name is "a" or "nobr" || FormattingTags.Contains(end.Name):
                RunAdoptionAgency(end.Name);
                return;

            case StartTagToken start when start.Name is "applet" or "marquee" or "object":
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                _activeFormatting.AddMarker();
                _framesetOk = false;
                return;

            case EndTagToken end when end.Name is "applet" or "marquee" or "object":
                if (!_openElements.HasInScope(end.Name))
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilNamed(end.Name);
                _activeFormatting.ClearToLastMarker();
                return;

            case StartTagToken { Name: "table" } start:
                if (_document.Mode != QuirksMode.Quirks)
                {
                    ClosePIfInButtonScope();
                }

                InsertHtmlElement(start);
                _framesetOk = false;
                _mode = InsertionMode.InTable;
                return;

            case StartTagToken start when start.Name is "area" or "br" or "embed" or "img" or "keygen" or "wbr":
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                _openElements.Pop();
                _framesetOk = false;
                return;

            case StartTagToken { Name: "input" } start:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                _openElements.Pop();
                {
                    var type = start.Attributes.FirstOrDefault(a => a.Name == "type")?.Value;
                    if (type is null || !type.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        _framesetOk = false;
                    }
                }
                return;

            case StartTagToken start when start.Name is "param" or "source" or "track":
                InsertHtmlElement(start);
                _openElements.Pop();
                return;

            case StartTagToken { Name: "hr" } start:
                ClosePIfInButtonScope();
                InsertHtmlElement(start);
                _openElements.Pop();
                _framesetOk = false;
                return;

            case StartTagToken { Name: "image" } start:
                HandleInBody(new StartTagToken("img", start.Attributes, start.SelfClosing));
                return;

            case StartTagToken { Name: "textarea" } start:
                InsertHtmlElement(start);
                _ignoreLf = true;
                _tokenizer.SetState(TokenizerState.Rcdata);
                _originalMode = _mode;
                _framesetOk = false;
                _mode = InsertionMode.Text;
                return;

            case StartTagToken { Name: "xmp" } start:
                ClosePIfInButtonScope();
                ReconstructActiveFormattingElements();
                _framesetOk = false;
                ParseGenericText(start, TokenizerState.Rawtext);
                return;

            case StartTagToken { Name: "iframe" } start:
                _framesetOk = false;
                ParseGenericText(start, TokenizerState.Rawtext);
                return;

            case StartTagToken start when start.Name == "noembed"
                                          || (start.Name == "noscript" && _scriptingEnabled):
                ParseGenericText(start, TokenizerState.Rawtext);
                return;

            case StartTagToken { Name: "select" } start:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                _framesetOk = false;
                _mode = _mode is InsertionMode.InTable or InsertionMode.InCaption
                    or InsertionMode.InTableBody or InsertionMode.InRow or InsertionMode.InCell
                    ? InsertionMode.InSelectInTable
                    : InsertionMode.InSelect;
                return;

            case StartTagToken start when start.Name is "optgroup" or "option":
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "option" })
                {
                    _openElements.Pop();
                }

                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                return;

            case StartTagToken start when start.Name is "rb" or "rtc":
                if (_openElements.HasInScope("ruby"))
                {
                    GenerateImpliedEndTags();
                }

                InsertHtmlElement(start);
                return;

            case StartTagToken start when start.Name is "rp" or "rt":
                if (_openElements.HasInScope("ruby"))
                {
                    GenerateImpliedEndTags(except: "rtc");
                }

                InsertHtmlElement(start);
                return;

            case StartTagToken { Name: "math" } start:
                ReconstructActiveFormattingElements();
                InsertForeignElement(start, MathMlNs);
                if (start.SelfClosing)
                {
                    _openElements.Pop();
                }

                return;

            case StartTagToken { Name: "svg" } start:
                ReconstructActiveFormattingElements();
                InsertForeignElement(start, SvgNs);
                if (start.SelfClosing)
                {
                    _openElements.Pop();
                }

                return;

            case StartTagToken start when start.Name is "caption" or "col" or "colgroup"
                or "frame" or "head" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                return; // parse error, ignore.

            case StartTagToken start:
                ReconstructActiveFormattingElements();
                InsertHtmlElement(start);
                return;

            case EndTagToken end:
                AnyOtherEndTag(end);
                return;
        }
    }

    private void StartListItem(StartTagToken start)
    {
        _framesetOk = false;
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node is { Namespace: HtmlNs, LocalName: "li" })
            {
                GenerateImpliedEndTags(except: "li");
                _openElements.PopUntilNamed("li");
                break;
            }
            if (IsSpecial(node) && node is not { Namespace: HtmlNs, LocalName: "address" or "div" or "p" })
            {
                break;
            }
        }
        ClosePIfInButtonScope();
        InsertHtmlElement(start);
    }

    private void StartDefinitionItem(StartTagToken start)
    {
        _framesetOk = false;
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node is { Namespace: HtmlNs, LocalName: "dd" or "dt" })
            {
                var name = node.LocalName;
                GenerateImpliedEndTags(except: name);
                _openElements.PopUntilNamed(name);
                break;
            }
            if (IsSpecial(node) && node is not { Namespace: HtmlNs, LocalName: "address" or "div" or "p" })
            {
                break;
            }
        }
        ClosePIfInButtonScope();
        InsertHtmlElement(start);
    }

    private void EndForm()
    {
        if (!_openElements.ContainsNamed("template"))
        {
            var node = _formElement;
            _formElement = null;
            if (node is null || !_openElements.HasInScope(node))
            {
                return;
            }

            GenerateImpliedEndTags();
            _openElements.Remove(node);
        }
        else
        {
            if (!_openElements.HasInScope("form"))
            {
                return;
            }

            GenerateImpliedEndTags();
            _openElements.PopUntilNamed("form");
        }
    }

    private void AnyOtherEndTag(EndTagToken end)
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            if (node.Namespace == HtmlNs && string.Equals(node.LocalName, end.Name, StringComparison.Ordinal))
            {
                GenerateImpliedEndTags(except: end.Name);
                _openElements.PopUntilElement(node);
                return;
            }
            if (IsSpecial(node))
            {
                return; // parse error, ignore.
            }
        }
    }

    private bool AnyHeadingInScope()
        => _openElements.HasInScope("h1") || _openElements.HasInScope("h2")
        || _openElements.HasInScope("h3") || _openElements.HasInScope("h4")
        || _openElements.HasInScope("h5") || _openElements.HasInScope("h6");

    // ---------------------------------------------------------------------- Text

    private void HandleText(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c:
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case EndOfFileToken:
                FlushText();
                if (!_openElements.IsEmpty)
                {
                    _openElements.Pop();
                }

                _mode = _originalMode;
                ProcessUsingInsertionMode(token);
                return;
            case EndTagToken:
                FlushText();
                _openElements.Pop();
                _mode = _originalMode;
                return;
        }
    }
}
