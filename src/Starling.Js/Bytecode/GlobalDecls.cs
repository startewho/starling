namespace Starling.Js.Bytecode;

/// <summary>
/// Constant-pool payload for <see cref="Opcode.GlobalDeclInstantiation"/>: the
/// declaration name sets §16.1.7 GlobalDeclarationInstantiation needs, gathered
/// once at compile time. <see cref="LetNames"/> / <see cref="ConstNames"/> are
/// the script's top-level lexical names (<c>class</c> counts as <c>let</c>);
/// <see cref="FuncNames"/> are the top-level function-declaration names;
/// <see cref="VarNames"/> are the remaining var-declared names (transitive,
/// not crossing function/class boundaries). For eval code the lexical arrays
/// are empty — an eval's lexicals live in its own environment, not the global
/// record.
/// </summary>
/// <remarks><c>Deletable</c> is true for eval chunks — §19.2.1.3 creates global
/// var/function bindings with CreateGlobalVarBinding(name, D=true), so they
/// are configurable (deletable); script bindings (§16.1.7) are not.</remarks>
/// <remarks><c>AnnexBFnNames</c> are §B.3.3.2/.3 block-level function names in
/// sloppy code: pre-declared as global vars LENIENTLY (skipped on any
/// conflict, never an error) so a read before the block evaluates yields
/// undefined instead of a ReferenceError.</remarks>
public sealed record GlobalDecls(
    string[] LetNames,
    string[] ConstNames,
    string[] VarNames,
    string[] FuncNames,
    string[] AnnexBFnNames,
    bool Deletable);
