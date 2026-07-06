namespace Starling.Js.Test262.Tests;

/// <summary>
/// Test262 corpus discovery + enumeration + the env-var config block used by
/// the runner (<see cref="Test262Tests"/>).
/// </summary>
internal static class Test262Corpus
{
    public static IEnumerable<string> EnumerateTests(string testDir, string[] dirs, string? filter, int max)
    {
        var count = 0;
        foreach (var sub in dirs)
        {
            var path = Path.Combine(testDir, sub);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.js", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                // _FIXTURE.js files are imported by module tests, never run directly.
                if (file.EndsWith("_FIXTURE.js", StringComparison.Ordinal))
                {
                    continue;
                }

                if (filter is not null && file.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                yield return file;
                if (max > 0 && ++count >= max)
                {
                    yield break;
                }
            }
        }
    }

    public static string Category(string relFile)
    {
        // relFile like "test/language/expressions/addition/foo.js" → "language/expressions"
        var parts = relFile.Replace('\\', '/').Split('/');
        // strip leading "test"
        var i = parts.Length > 0 && parts[0] == "test" ? 1 : 0;
        return parts.Length > i + 1 ? parts[i] + "/" + parts[i + 1] : (parts.Length > i ? parts[i] : relFile);
    }

    /// <summary>Walk up from the test binary to the repo and locate
    /// testdata/test262 (gitignored, fetched separately).</summary>
    public static string? LocateCorpus()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "testdata", "test262");
            if (Directory.Exists(Path.Combine(candidate, "test")))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
