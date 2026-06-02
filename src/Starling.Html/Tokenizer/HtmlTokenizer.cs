using System.Text;
using Starling.Html.InputStream;

namespace Starling.Html.Tokenizer;

/// <summary>
/// WHATWG HTML tokenizer. Partial class split by state family so each cluster
/// stays readable.
/// </summary>
/// <remarks>
/// <para>
/// API shape:
/// <list type="bullet">
///   <item>Push-driven: <see cref="Feed(System.ReadOnlySpan{char})"/> +
///         <see cref="EndOfInput"/>. The engine feeds bytes as they arrive
///         on the network; the tokenizer is restartable across chunks.</item>
///   <item>Pull-mode token reader: <see cref="ReadToken"/> returns the next
///         token, or <c>null</c> if blocked on more input.</item>
/// </list>
/// </para>
/// <para>
/// State-family layout:
/// <list type="bullet">
///   <item>Data and shared EOF handling live in this file.</item>
///   <item>Tag and attribute states live in <c>HtmlTokenizer.TagStates.cs</c>.</item>
///   <item>RCDATA/RAWTEXT, ScriptData, Comment/CDATA, Doctype, and Character
///         references each live in their own partial file.</item>
/// </list>
/// </para>
/// </remarks>
public sealed partial class HtmlTokenizer
{
    private readonly PreprocessedStream _stream = new();
    private readonly Queue<HtmlToken> _emitted = new();
    private readonly IParseErrorSink _errors;

    private TokenizerState _state = TokenizerState.Data;
    private bool _eofReached;
    private bool _eofProcessed;

    // Reconsume: when a state says "reconsume in state X", we set _state = X
    // and push the just-consumed code point back via _reconsume so the next
    // Step() picks it up instead of advancing the input. -2 = no reconsume.
    private const int NoReconsume = -2;
    private int _reconsume = NoReconsume;

    // Position tracking (1-based) for parse-error reporting.
    private int _line = 1;
    private int _column = 0;

    // --- Tag/attribute builder ---------------------------------------------
    private bool _tagIsEnd;
    private bool _tagSelfClosing;
    private readonly StringBuilder _tagName = new();
    private readonly List<HtmlAttribute> _tagAttrs = [];
    private readonly StringBuilder _attrName = new();
    private readonly StringBuilder _attrValue = new();

    // Most-recently-emitted start tag name. Used by RCDATA/RAWTEXT/Script
    // end-tag-name states to recognize an "appropriate" close (§13.2.5.11).
    private string? _lastStartTagName;

    // Temporary buffer for end-tag-matching attempts in RCDATA/RAWTEXT/Script,
    // for MarkupDeclarationOpen's bounded lookahead, and for named-character-
    // reference candidate matching.
    private readonly StringBuilder _tempBuffer = new();

    // --- Comment + doctype builders ----------------------------------------
    private readonly StringBuilder _commentData = new();
    private readonly StringBuilder _doctypeName = new();
    private bool _doctypeNameSet;
    private readonly StringBuilder _doctypePublicId = new();
    private bool _doctypePublicIdSet;
    private readonly StringBuilder _doctypeSystemId = new();
    private bool _doctypeSystemIdSet;
    private bool _doctypeForceQuirks;

    // --- Character-reference state -----------------------------------------
    // Spec §13.2.5.72: many states route to CharacterReference; the return
    // state is the state we go back to. When the return state is one of the
    // AttributeValue* variants, decoded chars are appended to the attribute
    // value buffer instead of being emitted as character tokens.
    private TokenizerState _returnState = TokenizerState.Data;
    private uint _charRefCode;
    private bool _charRefOverflowed;

    public HtmlTokenizer(IParseErrorSink? errorSink = null)
    {
        _errors = errorSink ?? IParseErrorSink.Null;
    }

    /// <summary>The current state. Exposed for tests; not for general use.</summary>
    internal TokenizerState State => _state;

    /// <summary>
    /// Tree-builder seam. After the tree builder inserts a <c>&lt;textarea&gt;</c>
    /// or <c>&lt;title&gt;</c>, it must put the tokenizer into RCDATA; for
    /// <c>&lt;style&gt;</c>/<c>&lt;xmp&gt;</c>/<c>&lt;iframe&gt;</c>/<c>&lt;noembed&gt;</c>,
    /// RAWTEXT; for <c>&lt;script&gt;</c>, ScriptData. The tokenizer
    /// has no schema of its own, so the trigger lives with the consumer.
    /// </summary>
    public void SetState(TokenizerState state) => _state = state;

    /// <summary>Push more input.</summary>
    public void Feed(ReadOnlySpan<char> chars) => _stream.Feed(chars);

    /// <summary>Signal end-of-input.</summary>
    public void EndOfInput()
    {
        _stream.EndOfInput();
        _eofReached = true;
    }

    /// <summary>
    /// Returns the next token, or <c>null</c> if the tokenizer needs more
    /// input. After an <see cref="EndOfFileToken"/> is returned, subsequent
    /// calls return <c>null</c> (EOF is terminal).
    /// </summary>
    public HtmlToken? ReadToken()
    {
        while (_emitted.Count == 0)
        {
            if (!Step()) return null;
        }
        return _emitted.Dequeue();
    }

    /// <summary>
    /// One state-machine step. Returns true if it consumed a code point or
    /// produced a token; false if blocked on more input or EOF processed.
    /// </summary>
    private bool Step()
    {
        if (_eofProcessed) return false;

        int c;
        if (_reconsume != NoReconsume)
        {
            c = _reconsume;
            _reconsume = NoReconsume;
        }
        else if (_stream.Remaining == 0)
        {
            if (!_eofReached) return false;
            return StepEof();
        }
        else
        {
            c = _stream.Read();
            TrackPosition(c);
        }

        Dispatch(_state, c);
        return true;
    }

    /// <summary>
    /// Route the code point to the current state's handler. Each cluster of
    /// states lives in a partial file; this switch is the rendezvous.
    /// </summary>
    private void Dispatch(TokenizerState state, int c)
    {
        switch (state)
        {
            case TokenizerState.Data:
                StepData(c);
                return;

            // Tag + attribute states.
            case TokenizerState.TagOpen:
            case TokenizerState.EndTagOpen:
            case TokenizerState.TagName:
            case TokenizerState.BeforeAttributeName:
            case TokenizerState.AttributeName:
            case TokenizerState.AfterAttributeName:
            case TokenizerState.BeforeAttributeValue:
            case TokenizerState.AttributeValueDoubleQuoted:
            case TokenizerState.AttributeValueSingleQuoted:
            case TokenizerState.AttributeValueUnquoted:
            case TokenizerState.AfterAttributeValueQuoted:
            case TokenizerState.SelfClosingStartTag:
                DispatchTagState(state, c);
                return;

            // RCDATA / RAWTEXT / PLAINTEXT clusters.
            case TokenizerState.Rcdata:
            case TokenizerState.RcdataLessThanSign:
            case TokenizerState.RcdataEndTagOpen:
            case TokenizerState.RcdataEndTagName:
            case TokenizerState.Rawtext:
            case TokenizerState.RawtextLessThanSign:
            case TokenizerState.RawtextEndTagOpen:
            case TokenizerState.RawtextEndTagName:
            case TokenizerState.Plaintext:
                DispatchRawState(state, c);
                return;

            // ScriptData cluster.
            case TokenizerState.ScriptData:
            case TokenizerState.ScriptDataLessThanSign:
            case TokenizerState.ScriptDataEndTagOpen:
            case TokenizerState.ScriptDataEndTagName:
            case TokenizerState.ScriptDataEscapeStart:
            case TokenizerState.ScriptDataEscapeStartDash:
            case TokenizerState.ScriptDataEscaped:
            case TokenizerState.ScriptDataEscapedDash:
            case TokenizerState.ScriptDataEscapedDashDash:
            case TokenizerState.ScriptDataEscapedLessThanSign:
            case TokenizerState.ScriptDataEscapedEndTagOpen:
            case TokenizerState.ScriptDataEscapedEndTagName:
            case TokenizerState.ScriptDataDoubleEscapeStart:
            case TokenizerState.ScriptDataDoubleEscaped:
            case TokenizerState.ScriptDataDoubleEscapedDash:
            case TokenizerState.ScriptDataDoubleEscapedDashDash:
            case TokenizerState.ScriptDataDoubleEscapedLessThanSign:
            case TokenizerState.ScriptDataDoubleEscapeEnd:
                DispatchScriptState(state, c);
                return;

            // Comment + CDATA + bogus-comment + markup-declaration-open.
            case TokenizerState.MarkupDeclarationOpen:
            case TokenizerState.CommentStart:
            case TokenizerState.CommentStartDash:
            case TokenizerState.Comment:
            case TokenizerState.CommentLessThanSign:
            case TokenizerState.CommentLessThanSignBang:
            case TokenizerState.CommentLessThanSignBangDash:
            case TokenizerState.CommentLessThanSignBangDashDash:
            case TokenizerState.CommentEndDash:
            case TokenizerState.CommentEnd:
            case TokenizerState.CommentEndBang:
            case TokenizerState.BogusComment:
            case TokenizerState.CdataSection:
            case TokenizerState.CdataSectionBracket:
            case TokenizerState.CdataSectionEnd:
                DispatchCommentState(state, c);
                return;

            // Character-reference states.
            case TokenizerState.CharacterReference:
            case TokenizerState.NamedCharacterReference:
            case TokenizerState.AmbiguousAmpersand:
            case TokenizerState.NumericCharacterReference:
            case TokenizerState.HexadecimalCharacterReferenceStart:
            case TokenizerState.DecimalCharacterReferenceStart:
            case TokenizerState.HexadecimalCharacterReference:
            case TokenizerState.DecimalCharacterReference:
            case TokenizerState.NumericCharacterReferenceEnd:
                DispatchCharRefState(state, c);
                return;

            // Doctype states.
            case TokenizerState.Doctype:
            case TokenizerState.BeforeDoctypeName:
            case TokenizerState.DoctypeName:
            case TokenizerState.AfterDoctypeName:
            case TokenizerState.AfterDoctypePublicKeyword:
            case TokenizerState.BeforeDoctypePublicIdentifier:
            case TokenizerState.DoctypePublicIdentifierDoubleQuoted:
            case TokenizerState.DoctypePublicIdentifierSingleQuoted:
            case TokenizerState.AfterDoctypePublicIdentifier:
            case TokenizerState.BetweenDoctypePublicAndSystemIdentifiers:
            case TokenizerState.AfterDoctypeSystemKeyword:
            case TokenizerState.BeforeDoctypeSystemIdentifier:
            case TokenizerState.DoctypeSystemIdentifierDoubleQuoted:
            case TokenizerState.DoctypeSystemIdentifierSingleQuoted:
            case TokenizerState.AfterDoctypeSystemIdentifier:
            case TokenizerState.BogusDoctype:
                DispatchDoctypeState(state, c);
                return;

            default:
                throw new NotImplementedException(
                    $"Tokenizer state '{state}' has no dispatch handler.");
        }
    }

    private bool StepEof()
    {
        switch (_state)
        {
            case TokenizerState.Data:
                _emitted.Enqueue(EndOfFileToken.Instance);
                _eofProcessed = true;
                return true;

            // EOF handling delegated to TagStates partial.
            case TokenizerState.TagOpen:
            case TokenizerState.EndTagOpen:
            case TokenizerState.TagName:
            case TokenizerState.BeforeAttributeName:
            case TokenizerState.AttributeName:
            case TokenizerState.AfterAttributeName:
            case TokenizerState.BeforeAttributeValue:
            case TokenizerState.AttributeValueDoubleQuoted:
            case TokenizerState.AttributeValueSingleQuoted:
            case TokenizerState.AttributeValueUnquoted:
            case TokenizerState.AfterAttributeValueQuoted:
            case TokenizerState.SelfClosingStartTag:
                StepTagEof();
                _eofProcessed = true;
                return true;

            // EOF handling delegated to RawStates partial.
            case TokenizerState.Rcdata:
            case TokenizerState.RcdataLessThanSign:
            case TokenizerState.RcdataEndTagOpen:
            case TokenizerState.RcdataEndTagName:
            case TokenizerState.Rawtext:
            case TokenizerState.RawtextLessThanSign:
            case TokenizerState.RawtextEndTagOpen:
            case TokenizerState.RawtextEndTagName:
            case TokenizerState.Plaintext:
                StepRawEof();
                _eofProcessed = true;
                return true;

            // EOF handling delegated to ScriptStates partial.
            case TokenizerState.ScriptData:
            case TokenizerState.ScriptDataLessThanSign:
            case TokenizerState.ScriptDataEndTagOpen:
            case TokenizerState.ScriptDataEndTagName:
            case TokenizerState.ScriptDataEscapeStart:
            case TokenizerState.ScriptDataEscapeStartDash:
            case TokenizerState.ScriptDataEscaped:
            case TokenizerState.ScriptDataEscapedDash:
            case TokenizerState.ScriptDataEscapedDashDash:
            case TokenizerState.ScriptDataEscapedLessThanSign:
            case TokenizerState.ScriptDataEscapedEndTagOpen:
            case TokenizerState.ScriptDataEscapedEndTagName:
            case TokenizerState.ScriptDataDoubleEscapeStart:
            case TokenizerState.ScriptDataDoubleEscaped:
            case TokenizerState.ScriptDataDoubleEscapedDash:
            case TokenizerState.ScriptDataDoubleEscapedDashDash:
            case TokenizerState.ScriptDataDoubleEscapedLessThanSign:
            case TokenizerState.ScriptDataDoubleEscapeEnd:
                StepScriptEof();
                _eofProcessed = true;
                return true;

            // EOF handling delegated to CommentStates partial.
            case TokenizerState.MarkupDeclarationOpen:
            case TokenizerState.CommentStart:
            case TokenizerState.CommentStartDash:
            case TokenizerState.Comment:
            case TokenizerState.CommentLessThanSign:
            case TokenizerState.CommentLessThanSignBang:
            case TokenizerState.CommentLessThanSignBangDash:
            case TokenizerState.CommentLessThanSignBangDashDash:
            case TokenizerState.CommentEndDash:
            case TokenizerState.CommentEnd:
            case TokenizerState.CommentEndBang:
            case TokenizerState.BogusComment:
            case TokenizerState.CdataSection:
            case TokenizerState.CdataSectionBracket:
            case TokenizerState.CdataSectionEnd:
                StepCommentEof();
                _eofProcessed = true;
                return true;

            // EOF handling delegated to CharRefStates partial.
            case TokenizerState.CharacterReference:
            case TokenizerState.NamedCharacterReference:
            case TokenizerState.AmbiguousAmpersand:
            case TokenizerState.NumericCharacterReference:
            case TokenizerState.HexadecimalCharacterReferenceStart:
            case TokenizerState.DecimalCharacterReferenceStart:
            case TokenizerState.HexadecimalCharacterReference:
            case TokenizerState.DecimalCharacterReference:
            case TokenizerState.NumericCharacterReferenceEnd:
                StepCharRefEof();
                _eofProcessed = true;
                return true;

            // EOF handling delegated to DoctypeStates partial.
            case TokenizerState.Doctype:
            case TokenizerState.BeforeDoctypeName:
            case TokenizerState.DoctypeName:
            case TokenizerState.AfterDoctypeName:
            case TokenizerState.AfterDoctypePublicKeyword:
            case TokenizerState.BeforeDoctypePublicIdentifier:
            case TokenizerState.DoctypePublicIdentifierDoubleQuoted:
            case TokenizerState.DoctypePublicIdentifierSingleQuoted:
            case TokenizerState.AfterDoctypePublicIdentifier:
            case TokenizerState.BetweenDoctypePublicAndSystemIdentifiers:
            case TokenizerState.AfterDoctypeSystemKeyword:
            case TokenizerState.BeforeDoctypeSystemIdentifier:
            case TokenizerState.DoctypeSystemIdentifierDoubleQuoted:
            case TokenizerState.DoctypeSystemIdentifierSingleQuoted:
            case TokenizerState.AfterDoctypeSystemIdentifier:
            case TokenizerState.BogusDoctype:
                StepDoctypeEof();
                _eofProcessed = true;
                return true;

            default:
                throw new NotImplementedException(
                    $"EOF in state '{_state}' has no handler.");
        }
    }

    // -----------------------------------------------------------------------
    // 13.2.5.1 Data state
    // https://html.spec.whatwg.org/multipage/parsing.html#data-state
    // -----------------------------------------------------------------------
    private void StepData(int c)
    {
        switch (c)
        {
            case '&':
                _returnState = TokenizerState.Data;
                _tempBuffer.Clear();
                _tempBuffer.Append('&');
                _state = TokenizerState.CharacterReference;
                break;

            case '<':
                _state = TokenizerState.TagOpen;
                break;

            case 0:
                // §13.2.5.1: "unexpected-null-character parse error. Emit the
                // current input character as a character token."
                _errors.Report(
                    HtmlParseError.UnexpectedNullCharacter, _line, _column);
                _emitted.Enqueue(new CharacterToken(0));
                break;

            default:
                _emitted.Enqueue(new CharacterToken(c));
                break;
        }
    }

    private void Reconsume(int c, TokenizerState next)
    {
        _state = next;
        _reconsume = c;
    }

    private void TrackPosition(int c)
    {
        if (c == '\n')
        {
            _line++;
            _column = 0;
        }
        else
        {
            _column++;
        }
    }

}
