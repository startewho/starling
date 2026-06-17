namespace Starling.Html.TreeBuilder;

/// <summary>
/// WHATWG HTML §13.2.4.1 insertion modes. The full set is implemented; foreign
/// content is handled inline (the spec's "in foreign content" rules run from
/// the dispatcher rather than as a distinct mode).
/// </summary>
internal enum InsertionMode : byte
{
    Initial,
    BeforeHtml,
    BeforeHead,
    InHead,
    InHeadNoscript,
    AfterHead,
    InBody,
    Text,
    InTable,
    InTableText,
    InCaption,
    InColumnGroup,
    InTableBody,
    InRow,
    InCell,
    InSelect,
    InSelectInTable,
    InTemplate,
    AfterBody,
    InFrameset,
    AfterFrameset,
    AfterAfterBody,
    AfterAfterFrameset,
}
