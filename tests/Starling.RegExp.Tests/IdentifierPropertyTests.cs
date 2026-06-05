using AwesomeAssertions;
namespace Starling.RegExp.Tests;

/// <summary>
/// B5: Unicode binary property escapes \p{ID_Start} / \p{ID_Continue} under /u.
/// </summary>
[TestClass]
public class IdentifierPropertyTests
{
    private static RegexMatch? Run(string pattern, string flags, string input, int start = 0)
    {
        RegexFlagParser.TryParse(flags, out var f, out _);
        var re = CompiledRegex.Compile(pattern, f);
        return re.Exec(input, start);
    }

    [TestMethod]
    public void IdStart_matches_identifier_start_chars()
    {
        Run("\\p{ID_Start}", "u", "a")!.Group(0).Should().Be("a");
        Run("\\p{ID_Start}", "u", "Z")!.Group(0).Should().Be("Z");
        Run("\\p{ID_Start}", "u", "$")!.Group(0).Should().Be("$");
        Run("\\p{ID_Start}", "u", "_")!.Group(0).Should().Be("_");
        Run("\\p{ID_Start}", "u", "π")!.Group(0).Should().Be("π"); // Greek small pi
    }

    [TestMethod]
    public void IdStart_rejects_non_identifier_start_chars()
    {
        Run("^\\p{ID_Start}$", "u", "1").Should().BeNull();
        Run("^\\p{ID_Start}$", "u", " ").Should().BeNull();
        Run("^\\p{ID_Start}$", "u", "-").Should().BeNull();
    }

    [TestMethod]
    public void IdContinue_matches_identifier_continue_chars()
    {
        Run("\\p{ID_Continue}", "u", "a")!.Group(0).Should().Be("a");
        Run("\\p{ID_Continue}", "u", "0")!.Group(0).Should().Be("0");
        Run("\\p{ID_Continue}", "u", "_")!.Group(0).Should().Be("_");
    }

    [TestMethod]
    public void IdContinue_rejects_non_identifier_chars()
    {
        Run("^\\p{ID_Continue}$", "u", " ").Should().BeNull();
        Run("^\\p{ID_Continue}$", "u", "-").Should().BeNull();
    }

    [TestMethod]
    public void Negated_IdStart_matches_complement()
    {
        // \P{ID_Start} is the negation: matches a digit/space/dash, not a letter.
        Run("^\\P{ID_Start}$", "u", "1")!.Group(0).Should().Be("1");
        Run("^\\P{ID_Start}$", "u", "-")!.Group(0).Should().Be("-");
        Run("^\\P{ID_Start}$", "u", "a").Should().BeNull();
    }
}
