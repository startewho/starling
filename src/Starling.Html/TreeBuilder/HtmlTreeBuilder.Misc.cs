using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    // --------------------------------------------------------------- InSelect

    private void HandleInSelect(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken { CodePoint: 0 }: return;
            case CharacterToken c: InsertCharacter(CodePointToString(c.CodePoint)); return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken { Name: "option" } start:
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "option" })
                {
                    _openElements.Pop();
                }

                InsertHtmlElement(start);
                return;
            case StartTagToken { Name: "optgroup" } start:
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "option" })
                {
                    _openElements.Pop();
                }

                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "optgroup" })
                {
                    _openElements.Pop();
                }

                InsertHtmlElement(start);
                return;
            case StartTagToken { Name: "hr" } start:
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "option" })
                {
                    _openElements.Pop();
                }

                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "optgroup" })
                {
                    _openElements.Pop();
                }

                InsertHtmlElement(start);
                _openElements.Pop();
                return;
            case EndTagToken { Name: "optgroup" }:
                if (_openElements.Count >= 2
                    && _openElements.Current is { Namespace: HtmlNs, LocalName: "option" }
                    && _openElements[^2] is { Namespace: HtmlNs, LocalName: "optgroup" })
                {
                    _openElements.Pop();
                }

                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "optgroup" })
                {
                    _openElements.Pop();
                }

                return;
            case EndTagToken { Name: "option" }:
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "option" })
                {
                    _openElements.Pop();
                }

                return;
            case EndTagToken { Name: "select" }:
                if (!_openElements.HasInSelectScope("select"))
                {
                    return;
                }

                _openElements.PopUntilNamed("select");
                ResetInsertionModeAppropriately();
                return;
            case StartTagToken { Name: "select" }:
                if (!_openElements.HasInSelectScope("select"))
                {
                    return;
                }

                _openElements.PopUntilNamed("select");
                ResetInsertionModeAppropriately();
                return;
            // Only <input> still breaks out of a select; under the customizable
            // <select> content model <keygen>/<textarea> are inserted (they fall
            // through to the in-body delegation below).
            case StartTagToken { Name: "input" }:
                if (!_openElements.HasInSelectScope("select"))
                {
                    return;
                }

                _openElements.PopUntilNamed("select");
                ResetInsertionModeAppropriately();
                HandleToken(token);
                return;
            case StartTagToken { Name: "script" or "template" }:
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;
            case EndOfFileToken:
                HandleInBody(token);
                return;
        }
        // Anything else: with the customizable-<select> content model, arbitrary
        // flow content (div/button/datalist/math/svg/formatting…) is inserted via
        // the in-body rules rather than dropped.
        HandleInBody(token);
    }

    private void HandleInSelectInTable(HtmlToken token)
    {
        switch (token)
        {
            case StartTagToken start when start.Name is "caption" or "table" or "tbody"
                or "tfoot" or "thead" or "tr" or "td" or "th":
                _openElements.PopUntilNamed("select");
                ResetInsertionModeAppropriately();
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "caption" or "table" or "tbody"
                or "tfoot" or "thead" or "tr" or "td" or "th":
                if (!_openElements.HasInTableScope(end.Name))
                {
                    return;
                }

                _openElements.PopUntilNamed("select");
                ResetInsertionModeAppropriately();
                HandleToken(token);
                return;
        }
        HandleInSelect(token);
    }

    // ------------------------------------------------------------- InTemplate

    private void HandleInTemplate(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken:
            case CommentToken:
            case DoctypeToken:
                HandleInBody(token);
                return;
            case StartTagToken start when start.Name is "base" or "basefont" or "bgsound"
                or "link" or "meta" or "noframes" or "script" or "style" or "template" or "title":
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;
            case StartTagToken start when start.Name is "caption" or "colgroup" or "tbody"
                or "tfoot" or "thead":
                SwitchTemplateMode(InsertionMode.InTable);
                HandleToken(token);
                return;
            case StartTagToken { Name: "col" }:
                SwitchTemplateMode(InsertionMode.InColumnGroup);
                HandleToken(token);
                return;
            case StartTagToken { Name: "tr" }:
                SwitchTemplateMode(InsertionMode.InTableBody);
                HandleToken(token);
                return;
            case StartTagToken start when start.Name is "td" or "th":
                SwitchTemplateMode(InsertionMode.InRow);
                HandleToken(token);
                return;
            case StartTagToken:
                SwitchTemplateMode(InsertionMode.InBody);
                HandleToken(token);
                return;
            case EndTagToken:
                return; // parse error, ignore.
            case EndOfFileToken:
                if (!_openElements.ContainsNamed("template"))
                {
                    return; // stop parsing.
                }

                _openElements.PopUntilNamed("template");
                _activeFormatting.ClearToLastMarker();
                if (_templateInsertionModes.Count > 0)
                {
                    _templateInsertionModes.Pop();
                }

                ResetInsertionModeAppropriately();
                HandleToken(token);
                return;
        }
    }

    private void SwitchTemplateMode(InsertionMode mode)
    {
        if (_templateInsertionModes.Count > 0)
        {
            _templateInsertionModes.Pop();
        }

        _templateInsertionModes.Push(mode);
        _mode = mode;
    }

    // ------------------------------------------------------------- InFrameset

    private void HandleInFrameset(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken { Name: "frameset" } start:
                InsertHtmlElement(start);
                return;
            case EndTagToken { Name: "frameset" }:
                if (_openElements.Current is { Namespace: HtmlNs, LocalName: "html" })
                {
                    return;
                }

                _openElements.Pop();
                if (_fragmentContext is null && _openElements.Current is not { Namespace: HtmlNs, LocalName: "frameset" })
                {
                    _mode = InsertionMode.AfterFrameset;
                }

                return;
            case StartTagToken { Name: "frame" } start:
                InsertHtmlElement(start);
                _openElements.Pop();
                return;
            case StartTagToken { Name: "noframes" }:
                HandleInHead(token);
                return;
            case EndOfFileToken:
                return; // stop parsing.
        }
        // Anything else: parse error, ignore.
    }

    private void HandleAfterFrameset(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case EndTagToken { Name: "html" }:
                _mode = InsertionMode.AfterAfterFrameset;
                return;
            case StartTagToken { Name: "noframes" }:
                HandleInHead(token);
                return;
            case EndOfFileToken:
                return;
        }
        // Anything else: parse error, ignore.
    }

    // ------------------------------------------------------------- AfterBody

    private void HandleAfterBody(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                HandleInBody(token);
                return;
            case CommentToken comment:
                InsertComment(comment, overrideParent: _openElements[0]);
                return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case EndTagToken { Name: "html" }:
                if (_fragmentContext is not null)
                {
                    return;
                }

                _mode = InsertionMode.AfterAfterBody;
                return;
            case EndOfFileToken:
                return;
        }
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }

    private void HandleAfterAfterBody(HtmlToken token)
    {
        switch (token)
        {
            case CommentToken comment: InsertComment(comment, overrideParent: _document); return;
            case DoctypeToken: HandleInBody(token); return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): HandleInBody(token); return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case EndOfFileToken: return;
        }
        _mode = InsertionMode.InBody;
        HandleInBody(token);
    }

    private void HandleAfterAfterFrameset(HtmlToken token)
    {
        switch (token)
        {
            case CommentToken comment: InsertComment(comment, overrideParent: _document); return;
            case DoctypeToken: HandleInBody(token); return;
            case CharacterToken c when IsWhitespaceChar(c.CodePoint): HandleInBody(token); return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case EndOfFileToken: return;
            case StartTagToken { Name: "noframes" }: HandleInHead(token); return;
        }
        // Anything else: parse error, ignore.
    }
}
