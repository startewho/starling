using System.Diagnostics;
using System.Text;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

/// <summary>
/// WHATWG HTML §13.2.6 tree construction. A faithful implementation of the
/// insertion-mode state machine: the document/head/body modes, the full table
/// mode family with foster parenting, select, template, frameset, the list of
/// active formatting elements with the adoption agency algorithm, and SVG/MathML
/// foreign content. Drives the html5lib tree-construction conformance corpus.
/// </summary>
public sealed partial class HtmlTreeBuilder
{
    internal const string HtmlNs = Element.HtmlNamespace;
    internal const string MathMlNs = "http://www.w3.org/1998/Math/MathML";
    internal const string SvgNs = "http://www.w3.org/2000/svg";
    internal const string XLinkNs = "http://www.w3.org/1999/xlink";
    internal const string XmlNs = "http://www.w3.org/XML/1998/namespace";
    internal const string XmlNsNs = "http://www.w3.org/2000/xmlns/";

    private readonly HtmlTokenizer _tokenizer;
    private readonly Document _document = new();
    private readonly StackOfOpenElements _openElements = new();
    private readonly ActiveFormattingElements _activeFormatting = new();
    private readonly CountingParseErrorSink? _errorCounter;
    private readonly bool _scriptingEnabled;

    // Coalesced character insertion. Characters accumulate here and flush to a
    // single Text node when a non-character token (or EOF) arrives, so a run of
    // character tokens becomes one DOM write rather than O(n²) string growth.
    private readonly StringBuilder _pending = new();
    private Node? _pendingParent;
    private Node? _pendingBefore;

    private Element? _headElement;
    private Element? _formElement;
    private InsertionMode _mode = InsertionMode.Initial;
    private InsertionMode _originalMode = InsertionMode.Initial;
    private readonly Stack<InsertionMode> _templateInsertionModes = new();
    private bool _framesetOk = true;
    private bool _fosterParenting;
    private bool _ignoreLf;
    private int _tokenCount;

    // InTableText buffering (§13.2.6.4.10). Collected separately from _pending
    // because the whole run is reclassified (whitespace vs not) on flush.
    private readonly StringBuilder _pendingTableText = new();
    private bool _pendingTableTextHasNonWhitespace;

    private readonly Element? _fragmentContext;

    public HtmlTreeBuilder(HtmlTokenizer tokenizer,
        CountingParseErrorSink? errorCounter = null, bool scriptingEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
        _errorCounter = errorCounter;
        _scriptingEnabled = scriptingEnabled;
    }

    private HtmlTreeBuilder(HtmlTokenizer tokenizer, Element fragmentContext,
        CountingParseErrorSink? errorCounter, bool scriptingEnabled)
        : this(tokenizer, errorCounter, scriptingEnabled)
    {
        _fragmentContext = fragmentContext;
    }

    public static Document Parse(string html, bool scriptingEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        var errorCounter = new CountingParseErrorSink();
        var tokenizer = new HtmlTokenizer(errorCounter);
        tokenizer.Feed(html);
        tokenizer.EndOfInput();
        var builder = new HtmlTreeBuilder(tokenizer, errorCounter, scriptingEnabled);
        tokenizer.CdataAllowed = builder.AdjustedCurrentNodeIsForeign;
        return builder.Run();
    }

    /// <summary>Tokenizer seam (§13.2.5.42): a <c>&lt;![CDATA[</c> is a real CDATA
    /// section only when the adjusted current node is a non-HTML element.</summary>
    private bool AdjustedCurrentNodeIsForeign()
        => AdjustedCurrentNode is { } acn && acn.Namespace != HtmlNs;

    /// <summary>
    /// HTML fragment parsing algorithm (§13.4). Parses <paramref name="markup"/>
    /// in the context of <paramref name="contextElement"/> and returns the
    /// resulting nodes as a detached <see cref="DocumentFragment"/>.
    /// </summary>
    public static DocumentFragment ParseFragment(string markup, Element contextElement,
        Document ownerDocument, bool scriptingEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(markup);
        ArgumentNullException.ThrowIfNull(contextElement);
        ArgumentNullException.ThrowIfNull(ownerDocument);

        var errorCounter = new CountingParseErrorSink();
        var tokenizer = new HtmlTokenizer(errorCounter);
        // §13.4 step 4: tokenizer state implied by the context element.
        if (contextElement.Namespace == HtmlNs)
            tokenizer.SetState(InitialTokenizerStateFor(contextElement.LocalName));
        tokenizer.Feed(markup);
        tokenizer.EndOfInput();

        var builder = new HtmlTreeBuilder(tokenizer, contextElement, errorCounter, scriptingEnabled);
        tokenizer.CdataAllowed = builder.AdjustedCurrentNodeIsForeign;
        return builder.RunFragment(ownerDocument);
    }

    private DocumentFragment RunFragment(Document ownerDocument)
    {
        using var _ = StarlingTelemetry.Span("html", "parse-fragment");

        // §13.4 steps 5-9: synthetic <html> root, push it, set up template mode
        // stack if the context is a template, then reset the insertion mode.
        var root = _document.CreateElement("html");
        _document.AppendChild(root);
        _openElements.Push(root);
        if (_fragmentContext!.Namespace == HtmlNs && _fragmentContext.LocalName == "template")
            _templateInsertionModes.Push(InsertionMode.InTemplate);
        _formElement = FindFormAncestor(_fragmentContext);
        ResetInsertionModeAppropriately();

        while (_tokenizer.ReadToken() is { } token)
        {
            _tokenCount++;
            HandleToken(token);
            if (token is EndOfFileToken) break;
        }
        FlushText();

        var fragment = ownerDocument.CreateDocumentFragment();
        var child = root.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            fragment.AppendChild(child);
            child = next;
        }
        return fragment;
    }

    private static Element? FindFormAncestor(Element context)
    {
        for (Node? n = context; n is not null; n = n.ParentNode)
            if (n is Element { Namespace: HtmlNs, LocalName: "form" } form)
                return form;
        return null;
    }

    private static TokenizerState InitialTokenizerStateFor(string contextLocalName)
        => contextLocalName switch
        {
            "title" or "textarea" => TokenizerState.Rcdata,
            "style" or "xmp" or "iframe" or "noembed" or "noframes" => TokenizerState.Rawtext,
            "script" => TokenizerState.ScriptData,
            "noscript" => TokenizerState.Rawtext,
            "plaintext" => TokenizerState.Plaintext,
            _ => TokenizerState.Data,
        };

    public Document Run()
    {
        using var _ = StarlingTelemetry.Span("html", "parse");
        try
        {
            while (_tokenizer.ReadToken() is { } token)
            {
                _tokenCount++;
                HandleToken(token);
                if (token is EndOfFileToken) break;
            }
            FlushText();

            var errorCount = _errorCounter?.Count ?? 0;
            Activity.Current?.SetTag("html.tokens", _tokenCount);
            Activity.Current?.SetTag("html.parse_errors", errorCount);
            StarlingTelemetry.Counter("html.parses", 1);
            if (errorCount > 0)
                StarlingTelemetry.Counter("html.parse_errors", errorCount);
            return _document;
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ------------------------------------------------------------ dispatcher

    private Element? AdjustedCurrentNode =>
        _fragmentContext is not null && _openElements.Count == 1
            ? _fragmentContext
            : _openElements.IsEmpty ? null : _openElements.Current;

    private void HandleToken(HtmlToken token)
    {
        // "ignore the next token if it is a U+000A LINE FEED" — set after a
        // <pre>/<listing>/<textarea> start tag (§13.2.6.4.7).
        if (_ignoreLf)
        {
            _ignoreLf = false;
            if (token is CharacterToken { CodePoint: '\n' }) return;
        }

        // §13.2.6 tree construction dispatcher. Decide between the current
        // insertion mode and the foreign-content rules.
        var acn = AdjustedCurrentNode;
        var useHtml =
            _openElements.IsEmpty ||
            acn!.Namespace == HtmlNs ||
            (IsMathMlTextIntegrationPoint(acn) && token is StartTagToken stMi && stMi.Name is not "mglyph" and not "malignmark") ||
            (IsMathMlTextIntegrationPoint(acn) && token is CharacterToken) ||
            (acn.Namespace == MathMlNs && acn.LocalName == "annotation-xml" && token is StartTagToken { Name: "svg" }) ||
            (IsHtmlIntegrationPoint(acn) && token is StartTagToken) ||
            (IsHtmlIntegrationPoint(acn) && token is CharacterToken) ||
            token is EndOfFileToken;

        if (token is not CharacterToken)
            FlushText();

        if (useHtml)
            ProcessUsingInsertionMode(token);
        else
            HandleForeignContent(token);
    }

    private void ProcessUsingInsertionMode(HtmlToken token)
    {
        switch (_mode)
        {
            case InsertionMode.Initial: HandleInitial(token); break;
            case InsertionMode.BeforeHtml: HandleBeforeHtml(token); break;
            case InsertionMode.BeforeHead: HandleBeforeHead(token); break;
            case InsertionMode.InHead: HandleInHead(token); break;
            case InsertionMode.InHeadNoscript: HandleInHeadNoscript(token); break;
            case InsertionMode.AfterHead: HandleAfterHead(token); break;
            case InsertionMode.InBody: HandleInBody(token); break;
            case InsertionMode.Text: HandleText(token); break;
            case InsertionMode.InTable: HandleInTable(token); break;
            case InsertionMode.InTableText: HandleInTableText(token); break;
            case InsertionMode.InCaption: HandleInCaption(token); break;
            case InsertionMode.InColumnGroup: HandleInColumnGroup(token); break;
            case InsertionMode.InTableBody: HandleInTableBody(token); break;
            case InsertionMode.InRow: HandleInRow(token); break;
            case InsertionMode.InCell: HandleInCell(token); break;
            case InsertionMode.InSelect: HandleInSelect(token); break;
            case InsertionMode.InSelectInTable: HandleInSelectInTable(token); break;
            case InsertionMode.InTemplate: HandleInTemplate(token); break;
            case InsertionMode.AfterBody: HandleAfterBody(token); break;
            case InsertionMode.InFrameset: HandleInFrameset(token); break;
            case InsertionMode.AfterFrameset: HandleAfterFrameset(token); break;
            case InsertionMode.AfterAfterBody: HandleAfterAfterBody(token); break;
            case InsertionMode.AfterAfterFrameset: HandleAfterAfterFrameset(token); break;
        }
    }

    // ------------------------------------------------- element construction

    private Element CreateElementForToken(StartTagToken token, string @namespace)
    {
        Element element = @namespace == HtmlNs
            ? _document.CreateElement(token.Name)
            : _document.CreateElementNS(@namespace, token.Name);
        foreach (var attr in token.Attributes)
            element.SetAttribute(attr.Name, attr.Value);
        if (@namespace == HtmlNs && token.Name == "form" && _formElement is null)
            _formElement = element;
        return element;
    }

    /// <summary>§13.2.6.1 "appropriate place for inserting a node", including
    /// foster parenting and template-content redirection.</summary>
    private (Node parent, Node? before) AppropriatePlaceForInserting(Node? overrideTarget = null, bool forceFoster = false)
    {
        var target = overrideTarget ?? _openElements.Current;

        Node parent;
        Node? before = null;
        if ((_fosterParenting || forceFoster) && target is Element te && IsFosterParentTarget(te))
        {
            // Foster parenting: find the last <template>/<table> on the stack.
            Element? lastTemplate = null;
            int lastTemplateIdx = -1, lastTableIdx = -1;
            Element? lastTable = null;
            for (var i = _openElements.Count - 1; i >= 0; i--)
            {
                var e = _openElements[i];
                if (lastTemplate is null && e is HtmlTemplateElement) { lastTemplate = e; lastTemplateIdx = i; }
                if (lastTable is null && e.Namespace == HtmlNs && e.LocalName == "table") { lastTable = e; lastTableIdx = i; }
            }

            if (lastTemplate is not null && (lastTable is null || lastTemplateIdx > lastTableIdx))
            {
                parent = ((HtmlTemplateElement)lastTemplate).Content;
            }
            else if (lastTable is null)
            {
                parent = _openElements[0]; // fragment case: the html element.
            }
            else if (lastTable.ParentNode is not null)
            {
                parent = lastTable.ParentNode;
                before = lastTable;
            }
            else
            {
                parent = _openElements[lastTableIdx - 1];
            }
        }
        else
        {
            parent = target;
        }

        if (parent is HtmlTemplateElement tmpl)
            parent = tmpl.Content;

        return (parent, before);
    }

    private static bool IsFosterParentTarget(Element e)
        => e.Namespace == HtmlNs && e.LocalName is "table" or "tbody" or "tfoot" or "thead" or "tr";

    private Element InsertHtmlElement(StartTagToken token)
    {
        var element = CreateElementForToken(token, HtmlNs);
        InsertElementAtAppropriatePlace(element);
        _openElements.Push(element);
        return element;
    }

    private void InsertElementAtAppropriatePlace(Element element)
    {
        var (parent, before) = AppropriatePlaceForInserting();
        parent.InsertBefore(element, before);
    }

    private void InsertCharacter(string data)
    {
        if (data.Length == 0) return;
        var (parent, before) = AppropriatePlaceForInserting();
        // Never create a text node directly under the Document.
        if (parent is Document) return;
        if (_pending.Length > 0 && (!ReferenceEquals(_pendingParent, parent) || !ReferenceEquals(_pendingBefore, before)))
            FlushText();
        if (_pending.Length == 0) { _pendingParent = parent; _pendingBefore = before; }
        _pending.Append(data);
    }

    private void FlushText()
    {
        if (_pending.Length == 0) return;
        var parent = _pendingParent!;
        var before = _pendingBefore;
        var data = _pending.ToString();
        _pending.Clear();
        _pendingParent = null;
        _pendingBefore = null;

        var prevNode = before is null ? parent.LastChild : before.PreviousSibling;
        if (prevNode is Text t)
            t.Data += data;
        else
            parent.InsertBefore(_document.CreateText(data), before);
    }

    private void InsertComment(CommentToken token, Node? overrideParent = null)
    {
        if (overrideParent is not null)
        {
            overrideParent.AppendChild(_document.CreateComment(token.Data));
            return;
        }
        var (parent, before) = AppropriatePlaceForInserting();
        parent.InsertBefore(_document.CreateComment(token.Data), before);
    }

    private void AppendCharToken(CharacterToken c) => InsertCharacter(CodePointToString(c.CodePoint));

    private static string CodePointToString(int codePoint)
        => codePoint <= char.MaxValue ? ((char)codePoint).ToString() : char.ConvertFromUtf32(codePoint);

    private static bool IsWhitespaceChar(int c)
        => c is '\t' or '\n' or '\f' or '\r' or ' ';

    // ------------------------------------------------------- shared algorithms

    private static readonly string[] ImpliedEndTagNames =
        ["dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc"];

    private void GenerateImpliedEndTags(string? except = null)
    {
        while (!_openElements.IsEmpty)
        {
            var cur = _openElements.Current;
            if (cur.Namespace != HtmlNs) return;
            var name = cur.LocalName;
            if (except is not null && name == except) return;
            if (Array.IndexOf(ImpliedEndTagNames, name) >= 0) _openElements.Pop();
            else return;
        }
    }

    private void GenerateImpliedEndTagsThoroughly()
    {
        while (!_openElements.IsEmpty)
        {
            var cur = _openElements.Current;
            if (cur.Namespace != HtmlNs) return;
            if (cur.LocalName is "caption" or "colgroup" or "dd" or "dt" or "li"
                or "optgroup" or "option" or "p" or "rb" or "rp" or "rt" or "rtc"
                or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr")
                _openElements.Pop();
            else return;
        }
    }

    private void ClosePIfInButtonScope()
    {
        if (_openElements.HasInButtonScope("p"))
            ClosePElement();
    }

    private void ClosePElement()
    {
        GenerateImpliedEndTags(except: "p");
        _openElements.PopUntilNamed("p");
    }

    /// <summary>§13.2.6.3 "reset the insertion mode appropriately".</summary>
    private void ResetInsertionModeAppropriately()
    {
        for (var i = _openElements.Count - 1; i >= 0; i--)
        {
            var node = _openElements[i];
            var last = i == 0;
            if (last && _fragmentContext is not null) node = _fragmentContext;

            if (node.Namespace == HtmlNs)
            {
                switch (node.LocalName)
                {
                    case "select":
                        if (!last)
                        {
                            for (var j = i - 1; j >= 1; j--)
                            {
                                var ancestor = _openElements[j];
                                if (ancestor is { Namespace: HtmlNs, LocalName: "template" }) break;
                                if (ancestor is { Namespace: HtmlNs, LocalName: "table" })
                                {
                                    _mode = InsertionMode.InSelectInTable;
                                    return;
                                }
                            }
                        }
                        _mode = InsertionMode.InSelect;
                        return;
                    case "td":
                    case "th":
                        if (!last) { _mode = InsertionMode.InCell; return; }
                        break;
                    case "tr":
                        _mode = InsertionMode.InRow;
                        return;
                    case "tbody":
                    case "thead":
                    case "tfoot":
                        _mode = InsertionMode.InTableBody;
                        return;
                    case "caption":
                        _mode = InsertionMode.InCaption;
                        return;
                    case "colgroup":
                        _mode = InsertionMode.InColumnGroup;
                        return;
                    case "table":
                        _mode = InsertionMode.InTable;
                        return;
                    case "template":
                        _mode = _templateInsertionModes.Count > 0 ? _templateInsertionModes.Peek() : InsertionMode.InBody;
                        return;
                    case "head":
                        if (!last) { _mode = InsertionMode.InHead; return; }
                        break;
                    case "body":
                        _mode = InsertionMode.InBody;
                        return;
                    case "frameset":
                        _mode = InsertionMode.InFrameset;
                        return;
                    case "html":
                        _mode = _headElement is null ? InsertionMode.BeforeHead : InsertionMode.AfterHead;
                        return;
                }
            }

            if (last)
            {
                _mode = InsertionMode.InBody;
                return;
            }
        }
    }

    /// <summary>§13.2.6.2 — quirks / limited-quirks / no-quirks from a DOCTYPE.</summary>
    private static QuirksMode DetermineQuirksMode(DoctypeToken d)
    {
        var name = d.Name ?? "";
        var pub = d.PublicId;
        var sys = d.SystemId;

        bool PubIs(string v) => pub is not null && pub.Equals(v, StringComparison.OrdinalIgnoreCase);
        bool PubStarts(string v) => pub is not null && pub.StartsWith(v, StringComparison.OrdinalIgnoreCase);

        if (d.ForceQuirks
            || !name.Equals("html", StringComparison.Ordinal)
            || PubIs("-//W3O//DTD W3 HTML Strict 3.0//EN//")
            || PubIs("-/W3C/DTD HTML 4.0 Transitional/EN")
            || PubIs("HTML")
            || (sys is not null && sys.Equals("http://www.ibm.com/data/dtd/v11/ibmxhtml1-transitional.dtd", StringComparison.OrdinalIgnoreCase))
            || PubStarts("+//Silmaril//dtd html Pro v0r11 19970101//")
            || PubStarts("-//AS//DTD HTML 3.0 asWedit + extensions//")
            || PubStarts("-//AdvaSoft Ltd//DTD HTML 3.0 asWedit + extensions//")
            || PubStarts("-//IETF//DTD HTML 2.0 Level 1//")
            || PubStarts("-//IETF//DTD HTML 2.0 Level 2//")
            || PubStarts("-//IETF//DTD HTML 2.0 Strict Level 1//")
            || PubStarts("-//IETF//DTD HTML 2.0 Strict Level 2//")
            || PubStarts("-//IETF//DTD HTML 2.0 Strict//")
            || PubStarts("-//IETF//DTD HTML 2.0//")
            || PubStarts("-//IETF//DTD HTML 2.1E//")
            || PubStarts("-//IETF//DTD HTML 3.0//")
            || PubStarts("-//IETF//DTD HTML 3.2 Final//")
            || PubStarts("-//IETF//DTD HTML 3.2//")
            || PubStarts("-//IETF//DTD HTML 3//")
            || PubStarts("-//IETF//DTD HTML Level 0//")
            || PubStarts("-//IETF//DTD HTML Level 1//")
            || PubStarts("-//IETF//DTD HTML Level 2//")
            || PubStarts("-//IETF//DTD HTML Level 3//")
            || PubStarts("-//IETF//DTD HTML Strict Level 0//")
            || PubStarts("-//IETF//DTD HTML Strict Level 1//")
            || PubStarts("-//IETF//DTD HTML Strict Level 2//")
            || PubStarts("-//IETF//DTD HTML Strict Level 3//")
            || PubStarts("-//IETF//DTD HTML Strict//")
            || PubStarts("-//IETF//DTD HTML//")
            || PubStarts("-//Metrius//DTD Metrius Presentational//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 2.0 HTML Strict//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 2.0 HTML//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 2.0 Tables//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 3.0 HTML Strict//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 3.0 HTML//")
            || PubStarts("-//Microsoft//DTD Internet Explorer 3.0 Tables//")
            || PubStarts("-//Netscape Comm. Corp.//DTD HTML//")
            || PubStarts("-//Netscape Comm. Corp.//DTD Strict HTML//")
            || PubStarts("-//O'Reilly and Associates//DTD HTML 2.0//")
            || PubStarts("-//O'Reilly and Associates//DTD HTML Extended 1.0//")
            || PubStarts("-//O'Reilly and Associates//DTD HTML Extended Relaxed 1.0//")
            || PubStarts("-//SQ//DTD HTML 2.0 HoTMetaL + extensions//")
            || PubStarts("-//SoftQuad Software//DTD HoTMetaL PRO 6.0::19990601::extensions to HTML 4.0//")
            || PubStarts("-//SoftQuad//DTD HoTMetaL PRO 4.0::19971010::extensions to HTML 4.0//")
            || PubStarts("-//Spyglass//DTD HTML 2.0 Extended//")
            || PubStarts("-//Sun Microsystems Corp.//DTD HotJava HTML//")
            || PubStarts("-//Sun Microsystems Corp.//DTD HotJava Strict HTML//")
            || PubStarts("-//W3C//DTD HTML 3 1995-03-24//")
            || PubStarts("-//W3C//DTD HTML 3.2 Draft//")
            || PubStarts("-//W3C//DTD HTML 3.2 Final//")
            || PubStarts("-//W3C//DTD HTML 3.2//")
            || PubStarts("-//W3C//DTD HTML 3.2S Draft//")
            || PubStarts("-//W3C//DTD HTML 4.0 Frameset//")
            || PubStarts("-//W3C//DTD HTML 4.0 Transitional//")
            || PubStarts("-//W3C//DTD HTML Experimental 19960712//")
            || PubStarts("-//W3C//DTD HTML Experimental 970421//")
            || PubStarts("-//W3C//DTD W3 HTML//")
            || PubStarts("-//W3O//DTD W3 HTML 3.0//")
            || PubStarts("-//WebTechs//DTD Mozilla HTML 2.0//")
            || PubStarts("-//WebTechs//DTD Mozilla HTML//")
            || (sys is null && PubStarts("-//W3C//DTD HTML 4.01 Frameset//"))
            || (sys is null && PubStarts("-//W3C//DTD HTML 4.01 Transitional//")))
            return QuirksMode.Quirks;

        if (PubStarts("-//W3C//DTD XHTML 1.0 Frameset//")
            || PubStarts("-//W3C//DTD XHTML 1.0 Transitional//")
            || (sys is not null && PubStarts("-//W3C//DTD HTML 4.01 Frameset//"))
            || (sys is not null && PubStarts("-//W3C//DTD HTML 4.01 Transitional//")))
            return QuirksMode.LimitedQuirks;

        return QuirksMode.NoQuirks;
    }

    private static void MergeAttributesInto(Element? target, StartTagToken start)
    {
        if (target is null) return;
        foreach (var attr in start.Attributes)
            if (!target.HasAttribute(attr.Name))
                target.SetAttribute(attr.Name, attr.Value);
    }

    private static StartTagToken Synthetic(string name)
        => new(name, Array.Empty<HtmlAttribute>(), SelfClosing: false);
}
