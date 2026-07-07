// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace Starling.Js.Test262.Tests;

internal static class Test262WorkerProtocol
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static string Encode(ScenarioResult result)
    {
        var detail = result.Detail is null ? "" : Convert.ToBase64String(Utf8.GetBytes(result.Detail));
        return string.Join('\t', result.File, (int)result.Mode, (int)result.Outcome, detail);
    }

    public static bool TryDecode(string line, out ScenarioResult result)
    {
        result = default!;
        var parts = line.Split('\t');
        if (parts.Length != 4
            || !int.TryParse(parts[1], out var mode)
            || !int.TryParse(parts[2], out var outcome)
            || !Enum.IsDefined(typeof(ScenarioMode), mode)
            || !Enum.IsDefined(typeof(Outcome), outcome))
        {
            return false;
        }

        string? detail = null;
        if (parts[3].Length > 0)
        {
            try
            {
                detail = Utf8.GetString(Convert.FromBase64String(parts[3]));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        result = new ScenarioResult(parts[0], (ScenarioMode)mode, (Outcome)outcome, detail);
        return true;
    }
}
