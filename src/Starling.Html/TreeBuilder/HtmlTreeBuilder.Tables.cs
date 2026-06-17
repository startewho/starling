using Starling.Dom;
using Starling.Html.Tokenizer;

namespace Starling.Html.TreeBuilder;

public sealed partial class HtmlTreeBuilder
{
    private void ClearStackToTableContext()
    {
        while (_openElements.Current is not ({ Namespace: HtmlNs, LocalName: "table" or "html" } or HtmlTemplateElement))
        {
            _openElements.Pop();
        }
    }

    private void ClearStackToTableBodyContext()
    {
        while (_openElements.Current is not ({ Namespace: HtmlNs, LocalName: "tbody" or "tfoot" or "thead" or "html" } or HtmlTemplateElement))
        {
            _openElements.Pop();
        }
    }

    private void ClearStackToTableRowContext()
    {
        while (_openElements.Current is not ({ Namespace: HtmlNs, LocalName: "tr" or "html" } or HtmlTemplateElement))
        {
            _openElements.Pop();
        }
    }

    // ---------------------------------------------------------------- InTable

    private void HandleInTable(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken when _openElements.Current is { Namespace: HtmlNs, LocalName: "table" or "tbody" or "tfoot" or "thead" or "tr" }:
                _pendingTableText.Clear();
                _pendingTableTextHasNonWhitespace = false;
                _originalMode = _mode;
                _mode = InsertionMode.InTableText;
                HandleInTableText(token);
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;

            case StartTagToken { Name: "caption" } start:
                ClearStackToTableContext();
                _activeFormatting.AddMarker();
                InsertHtmlElement(start);
                _mode = InsertionMode.InCaption;
                return;
            case StartTagToken { Name: "colgroup" } start:
                ClearStackToTableContext();
                InsertHtmlElement(start);
                _mode = InsertionMode.InColumnGroup;
                return;
            case StartTagToken { Name: "col" }:
                ClearStackToTableContext();
                InsertHtmlElement(Synthetic("colgroup"));
                _mode = InsertionMode.InColumnGroup;
                HandleInColumnGroup(token);
                return;
            case StartTagToken start when start.Name is "tbody" or "tfoot" or "thead":
                ClearStackToTableContext();
                InsertHtmlElement(start);
                _mode = InsertionMode.InTableBody;
                return;
            case StartTagToken start when start.Name is "td" or "th" or "tr":
                ClearStackToTableContext();
                InsertHtmlElement(Synthetic("tbody"));
                _mode = InsertionMode.InTableBody;
                HandleInTableBody(token);
                return;
            case StartTagToken { Name: "table" }:
                if (!_openElements.HasInTableScope("table"))
                {
                    return;
                }

                _openElements.PopUntilNamed("table");
                ResetInsertionModeAppropriately();
                HandleToken(token);
                return;
            case EndTagToken { Name: "table" }:
                if (!_openElements.HasInTableScope("table"))
                {
                    return;
                }

                _openElements.PopUntilNamed("table");
                ResetInsertionModeAppropriately();
                return;
            case EndTagToken end when end.Name is "body" or "caption" or "col" or "colgroup"
                or "html" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                return;
            case StartTagToken start when start.Name is "style" or "script" or "template":
                HandleInHead(token);
                return;
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;
            case StartTagToken { Name: "input" } start:
                {
                    var type = start.Attributes.FirstOrDefault(a => a.Name == "type")?.Value;
                    if (type is not null && type.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                    {
                        InsertHtmlElement(start);
                        _openElements.Pop();
                        return;
                    }
                }
                break; // anything else.
            case StartTagToken { Name: "form" } start:
                if (_openElements.ContainsNamed("template") || _formElement is not null)
                {
                    return;
                }
                {
                    var form = InsertHtmlElement(start);
                    _formElement = form;
                    _openElements.Pop();
                }
                return;
            case EndOfFileToken:
                HandleInBody(token);
                return;
        }

        // Anything else: foster-parent through InBody.
        _fosterParenting = true;
        HandleInBody(token);
        _fosterParenting = false;
    }

    private void HandleInTableText(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken { CodePoint: 0 }:
                return; // parse error, ignore.
            case CharacterToken c:
                _pendingTableText.Append(CodePointToString(c.CodePoint));
                if (!IsWhitespaceChar(c.CodePoint))
                {
                    _pendingTableTextHasNonWhitespace = true;
                }

                return;
        }

        // Anything else: flush the buffered characters, then reprocess.
        var text = _pendingTableText.ToString();
        _pendingTableText.Clear();
        if (_pendingTableTextHasNonWhitespace)
        {
            // Process as the "anything else" of InTable for each character.
            _fosterParenting = true;
            ReconstructActiveFormattingElements();
            InsertCharacter(text);
            FlushText();
            _framesetOk = false;
            _fosterParenting = false;
        }
        else
        {
            InsertCharacter(text);
            FlushText();
        }
        _pendingTableTextHasNonWhitespace = false;
        _mode = _originalMode;
        ProcessUsingInsertionMode(token);
    }

    // -------------------------------------------------------------- InCaption

    private void HandleInCaption(HtmlToken token)
    {
        switch (token)
        {
            case EndTagToken { Name: "caption" }:
                if (!_openElements.HasInTableScope("caption"))
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilNamed("caption");
                _activeFormatting.ClearToLastMarker();
                _mode = InsertionMode.InTable;
                return;
            case StartTagToken start when start.Name is "caption" or "col" or "colgroup"
                or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
            case EndTagToken { Name: "table" }:
                if (!_openElements.HasInTableScope("caption"))
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilNamed("caption");
                _activeFormatting.ClearToLastMarker();
                _mode = InsertionMode.InTable;
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "body" or "col" or "colgroup" or "html"
                or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                return;
        }
        HandleInBody(token);
    }

    // ----------------------------------------------------------- InColumnGroup

    private void HandleInColumnGroup(HtmlToken token)
    {
        switch (token)
        {
            case CharacterToken c when IsWhitespaceChar(c.CodePoint):
                InsertCharacter(CodePointToString(c.CodePoint));
                return;
            case CommentToken comment: InsertComment(comment); return;
            case DoctypeToken: return;
            case StartTagToken { Name: "html" }: HandleInBody(token); return;
            case StartTagToken { Name: "col" } start:
                InsertHtmlElement(start);
                _openElements.Pop();
                return;
            case EndTagToken { Name: "colgroup" }:
                if (_openElements.Current is not { Namespace: HtmlNs, LocalName: "colgroup" })
                {
                    return;
                }

                _openElements.Pop();
                _mode = InsertionMode.InTable;
                return;
            case EndTagToken { Name: "col" }:
                return;
            case StartTagToken { Name: "template" }:
            case EndTagToken { Name: "template" }:
                HandleInHead(token);
                return;
            case EndOfFileToken:
                HandleInBody(token);
                return;
        }

        if (_openElements.Current is not { Namespace: HtmlNs, LocalName: "colgroup" })
        {
            return;
        }

        _openElements.Pop();
        _mode = InsertionMode.InTable;
        HandleToken(token);
    }

    // ------------------------------------------------------------ InTableBody

    private void HandleInTableBody(HtmlToken token)
    {
        switch (token)
        {
            case StartTagToken { Name: "tr" } start:
                ClearStackToTableBodyContext();
                InsertHtmlElement(start);
                _mode = InsertionMode.InRow;
                return;
            case StartTagToken start when start.Name is "th" or "td":
                ClearStackToTableBodyContext();
                InsertHtmlElement(Synthetic("tr"));
                _mode = InsertionMode.InRow;
                HandleInRow(token);
                return;
            case EndTagToken end when end.Name is "tbody" or "tfoot" or "thead":
                if (!_openElements.HasInTableScope(end.Name))
                {
                    return;
                }

                ClearStackToTableBodyContext();
                _openElements.Pop();
                _mode = InsertionMode.InTable;
                return;
            case StartTagToken start when start.Name is "caption" or "col" or "colgroup"
                or "tbody" or "tfoot" or "thead":
            case EndTagToken { Name: "table" }:
                if (!_openElements.HasInTableScope("tbody") && !_openElements.HasInTableScope("thead")
                    && !_openElements.HasInTableScope("tfoot"))
                {
                    return;
                }

                ClearStackToTableBodyContext();
                _openElements.Pop();
                _mode = InsertionMode.InTable;
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "body" or "caption" or "col" or "colgroup"
                or "html" or "td" or "th" or "tr":
                return;
        }
        HandleInTable(token);
    }

    // ----------------------------------------------------------------- InRow

    private void HandleInRow(HtmlToken token)
    {
        switch (token)
        {
            case StartTagToken start when start.Name is "th" or "td":
                ClearStackToTableRowContext();
                InsertHtmlElement(start);
                _mode = InsertionMode.InCell;
                _activeFormatting.AddMarker();
                return;
            case EndTagToken { Name: "tr" }:
                if (!_openElements.HasInTableScope("tr"))
                {
                    return;
                }

                ClearStackToTableRowContext();
                _openElements.Pop();
                _mode = InsertionMode.InTableBody;
                return;
            case StartTagToken start when start.Name is "caption" or "col" or "colgroup"
                or "tbody" or "tfoot" or "thead" or "tr":
            case EndTagToken { Name: "table" }:
                if (!_openElements.HasInTableScope("tr"))
                {
                    return;
                }

                ClearStackToTableRowContext();
                _openElements.Pop();
                _mode = InsertionMode.InTableBody;
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "tbody" or "tfoot" or "thead":
                if (!_openElements.HasInTableScope(end.Name))
                {
                    return;
                }

                if (!_openElements.HasInTableScope("tr"))
                {
                    return;
                }

                ClearStackToTableRowContext();
                _openElements.Pop();
                _mode = InsertionMode.InTableBody;
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "body" or "caption" or "col" or "colgroup"
                or "html" or "td" or "th":
                return;
        }
        HandleInTable(token);
    }

    // ----------------------------------------------------------------- InCell

    private void CloseCell()
    {
        GenerateImpliedEndTags();
        _openElements.PopUntilOneOf("td", "th");
        _activeFormatting.ClearToLastMarker();
        _mode = InsertionMode.InRow;
    }

    private void HandleInCell(HtmlToken token)
    {
        switch (token)
        {
            case EndTagToken end when end.Name is "td" or "th":
                if (!_openElements.HasInTableScope(end.Name))
                {
                    return;
                }

                GenerateImpliedEndTags();
                _openElements.PopUntilNamed(end.Name);
                _activeFormatting.ClearToLastMarker();
                _mode = InsertionMode.InRow;
                return;
            case StartTagToken start when start.Name is "caption" or "col" or "colgroup"
                or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                if (!_openElements.HasInTableScope("td") && !_openElements.HasInTableScope("th"))
                {
                    return;
                }

                CloseCell();
                HandleToken(token);
                return;
            case EndTagToken end when end.Name is "body" or "caption" or "col" or "colgroup" or "html":
                return;
            case EndTagToken end when end.Name is "table" or "tbody" or "tfoot" or "thead" or "tr":
                if (!_openElements.HasInTableScope(end.Name))
                {
                    return;
                }

                CloseCell();
                HandleToken(token);
                return;
        }
        HandleInBody(token);
    }
}
