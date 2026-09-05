using System.Text;

namespace DarkFactory.Core;

/// <summary>A file the implementer wants to exist with this exact content.</summary>
public sealed record FileWrite(string Path, string Content);

/// <summary>
/// Builds a unified diff from before/after file contents.
///
/// <para>The factory generates the patch; the model does not. Asking a
/// language model to emit a unified diff means asking it to compute line
/// offsets and hunk headers, which it does plausibly and often wrongly —
/// and a patch that is wrong by one line fails to apply for reasons nobody
/// can see in the output. The model is asked for file <em>content</em>,
/// which it is good at, and the arithmetic is done here, where it is
/// arithmetic.</para>
///
/// <para>Hunks are whole-file: one hunk replacing every line. That produces
/// a larger patch than a minimal diff would, and it does not matter — the
/// patch is machine-generated, machine-applied, and stored by reference.
/// What it buys is that there is no diff algorithm to be subtly wrong, and
/// `git apply --3way` still merges it against a tree that has moved.</para>
/// </summary>
public static class UnifiedDiff
{
    public static string Build(IReadOnlyList<(string Path, string? Before, string After)> files)
    {
        var patch = new StringBuilder();

        foreach (var (path, before, after) in files)
        {
            if (before is not null && Normalize(before) == Normalize(after))
            {
                // Nothing changed. Emitting an empty hunk would produce a
                // patch git rejects as corrupt.
                continue;
            }

            AppendFile(patch, path, before, after);
        }

        return patch.ToString();
    }

    private static void AppendFile(StringBuilder patch, string path, string? before, string after)
    {
        var beforeLines = before is null ? [] : SplitLines(before);
        var afterLines = SplitLines(after);

        patch.Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n');

        if (before is null)
        {
            patch.Append("new file mode 100644\n");
            patch.Append("--- /dev/null\n");
        }
        else
        {
            patch.Append("--- a/").Append(path).Append('\n');
        }

        patch.Append("+++ b/").Append(path).Append('\n');

        // A zero-length side is written as "0,0" — "1,0" is what git emits
        // for an empty file and means something different.
        var beforeHeader = beforeLines.Count == 0 ? "0,0" : $"1,{beforeLines.Count}";
        var afterHeader = afterLines.Count == 0 ? "0,0" : $"1,{afterLines.Count}";
        patch.Append("@@ -").Append(beforeHeader).Append(" +").Append(afterHeader).Append(" @@\n");

        foreach (var line in beforeLines)
        {
            patch.Append('-').Append(line).Append('\n');
        }
        foreach (var line in afterLines)
        {
            patch.Append('+').Append(line).Append('\n');
        }
    }

    /// <summary>
    /// Splits into lines without inventing a trailing empty one. A file
    /// ending in a newline has N lines, not N+1, and getting that wrong
    /// makes every generated hunk header off by one.
    /// </summary>
    private static List<string> SplitLines(string content)
    {
        var normalized = Normalize(content);
        if (normalized.Length == 0)
        {
            return [];
        }

        var lines = normalized.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }
        return lines;
    }

    private static string Normalize(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
