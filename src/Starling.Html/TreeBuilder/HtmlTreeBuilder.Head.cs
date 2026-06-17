using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    private void ParseGenericText(StartTagToken token, TokenizerState state)
    {
        InsertHtmlElement(token);
        _tokenizer.SetState(state);
        _originalMode = _mode;
        _mode = InsertionMode.Text;
    }

    // --------------------------------------------------------------- Initial

    private void HandleInitial(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                return;
            case CommentToken comment:
                InsertComment(comment, overrideParent: _document);
                return;
            case DoctypeToken doctype:
                _document.AppendChild(_document.CreateDocumentType(
                    doctype.Name ?? "", doctype.PublicId ?? "", doctype.SystemId ?? ""));
                _document.Mode = DetermineQuirksMode(doctype);
                _mode = InsertionMode.BeforeHtml;
                return;
        }

        _document.Mode = QuirksMode.Quirks;
        _mode = InsertionMode.BeforeHtml;
        HandleBeforeHtml(token);
    }

    // ------------------------------------------------------------ BeforeHtml

    private void HandleBeforeHtml(HtmlToken token)
    {
        switch (token)
        {
            case DoctypeToken: return;
            case CommentToken comment: InsertComment(comment, overrideParent: _document); return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): return;
            case StartTagToken { Name: "html" } start:
                {
                    var html = CreateElementForToken(start, HtmlNs);
                    _document.AppendChild(html);
                    _openElements.Push(html);
                    _mode = InsertionMode.BeforeHead;
                    return;
                }
            case EndTagToken end when end.Name is "head" or "body" or "html" or "br":
                break;
            case EndTagToken: return;
        }

        var implicitHtml = _document.CreateElement("html");
        _document.AppendChild(implicitHtml);
        _openElements.Push(implicitHtml);
        _mode = InsertionMode.BeforeHead;
        HandleBeforeHead(token);
    }

    // ------------------------------------------------------------ BeforeHead

    private void HandleBeforeHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken { Name: "head" } start:
                _headElement = InsertHtmlElement(start);
                _mode = InsertionMode.InHead;
                return;
            case EndTagToken end when end.Name is "head" or "body" or "html" or "br":
                break;
            case EndTagToken: return;
        }

        _headElement = InsertHtmlElement(Synthetic("head"));
        _mode = InsertionMode.InHead;
        HandleInHead(token);
    }

    // --------------------------------------------------------------- InHead

    private void HandleInHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound" or "link" or "meta":
                InsertHtmlElement(start);
                _openElements.Pop();
                return;
            case StartTagToken start when start.Name == "title":
                ParseGenericText(start, TokenizerState.Rcdata);
                return;
            case StartTagToken start when start.Name == "noscript" && _scriptingEnabled:
                ParseGenericText(start, TokenizerState.Rawtext);
                return;
            case StartTagToken start when start.Name == "noscript":
                InsertHtmlElement(start);
                _mode = InsertionMode.InHeadNoscript;
                return;
            case StartTagToken start when start.Name is "noframes" or "style":
                ParseGenericText(start, TokenizerState.Rawtext);
                return;
            case StartTagToken start when start.Name == "script":
                ParseGenericText(start, TokenizerState.ScriptData);
                return;
            case StartTagToken start when start.Name == "template":
                InsertHtmlElement(start);
                _activeFormatting.AddMarker();
                _framesetOk = false;
                _mode = InsertionMode.InTemplate;
                _templateInsertionModes.Push(InsertionMode.InTemplate);
                return;
            case EndTagToken { Name: "template" }:
                HandleTemplateEndTag();
                return;
            case EndTagToken { Name: "head" }:
                _openElements.Pop();
                _mode = InsertionMode.AfterHead;
                return;
            case EndTagToken end when end.Name is "body" or "html" or "br":
                break;
            case StartTagToken { Name: "head" }: return;
            case EndTagToken: return;
        }

        _openElements.Pop();
        _mode = InsertionMode.AfterHead;
        HandleAfterHead(token);
    }

    // ----------------------------------------------------- InHeadNoscript

    private void HandleInHeadNoscript(HtmlToken token)
    {
        switch (token)
        {
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case EndTagToken { Name: "noscript" }:
                _openElements.Pop();
                _mode = InsertionMode.InHead;
                return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): HandleInHead(token); return;
            case CommentToken: HandleInHead(token); return;
            case StartTagToken start when start.Name is "basefont" or "bgsound" or "link"
                                                       or "meta" or "noframes" or "style":
                HandleInHead(token);
                return;
            case EndTagToken { Name: "br" }:
                break;
            case StartTagToken { Name: "head" or "noscript" }: return;
        }

        // Anything else: parse error, pop the noscript, reprocess in InHead.
        _openElements.Pop();
        _mode = InsertionMode.InHead;
        HandleInHead(token);
    }

    // -------------------------------------------------------------- AfterHead

    private void HandleAfterHead(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken { Name: "body" } start:
                InsertHtmlElement(start);
                _framesetOk = false;
                _mode = InsertionMode.InBody;
                return;
            case StartTagToken { Name: "frameset" } start:
                InsertHtmlElement(start);
                _mode = InsertionMode.InFrameset;
                return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound"
                                                       or "link" or "meta" or "noframes"
                                                       or "script" or "style" or "template"
                                                       or "title":
                if (_headElement is not null)
                {
                    _openElements.Push(_headElement);
                }

                HandleInHead(token);
                if (_headElement is not null)
                {
                    _openElements.Remove(_headElement);
                }

                return;
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;
            case EndTagToken end when end.Name is "body" or "html" or "br":
                break;
            case StartTagToken { Name: "head" }: return;
            case EndTagToken: return;
        }

        InsertHtmlElement(Synthetic("body"));
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }

    /// <summary>§13.2.6.4.4 — the "in head" / "in template" &lt;/template&gt;
    /// end-tag steps, shared by every mode that delegates template closing.</summary>
    private void HandleTemplateEndTag()
    {
        if (!_openElements.ContainsNamed("template"))
        {
            return; // parse error: ignore.
        }

        GenerateImpliedEndTagsThoroughly();
        _openElements.PopUntilNamed("template");
        _activeFormatting.ClearToLastMarker();
        if (_templateInsertionModes.Count > 0)
        {
            _templateInsertionModes.Pop();
        }

        ResetInsertionModeAppropriately();
    }
}
