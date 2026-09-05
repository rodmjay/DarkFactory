using System.Text.RegularExpressions;

namespace DarkFactory.Core;

/// <summary>
/// How a spec id is carried in generated code, and how the factory checks
/// that it was (docs/adr/0024).
///
/// The reference is what makes traceability bidirectional and drift
/// computable: spec → implementing symbols, symbol → justifying spec. Since
/// the factory generates the code, it applies the references itself rather
/// than hoping — and then verifies its own work, because an implement stage
/// that quietly dropped them would leave a run that looks successful and a
/// codebase that has silently lost its link to the graph.
/// </summary>
public static partial class SpecReferences
{
    /// <summary>
    /// A ULID as it appears in code. Matched on its own so a reference to a
    /// spec that does not exist is detectable, rather than only references
    /// we expected being found.
    /// </summary>
    [GeneratedRegex(@"[0-9A-HJKMNP-TV-Z]{26}")]
    private static partial Regex UlidPattern();

    /// <summary>
    /// How a given file carries a reference. Each standards server will
    /// eventually declare this for its layer (docs/adr/0024); until then the
    /// factory knows two forms, which is enough for the languages it
    /// generates.
    /// </summary>
    public static string Format(string path, string specId) =>
        IsCSharp(path) ? $"[Spec(\"{specId}\")]" : $"{CommentPrefix(path)} df:spec {specId}";

    /// <summary>The instruction given to the implementer, derived from the same rules the check applies.</summary>
    public static string Guidance =>
        """
        Every file you write must carry the spec ids it implements:
          - C# (.cs): a [Spec("<spec_id>")] attribute on the type or member the spec governs.
          - Anything else: a `df:spec <spec_id>` comment, using that file's comment syntax.
        Use only spec ids from the specifications listed above. Do not invent one, and do
        not reference a spec that is not in the list — a spec id that is not in the
        snapshot will be rejected.
        """;

    public static bool IsCSharp(string path) => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    private static string CommentPrefix(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".sql" => "--",
        ".py" or ".sh" or ".yml" or ".yaml" or ".tf" => "#",
        _ => "//",
    };

    /// <summary>Every ULID-shaped token appearing in the content, whatever form it was written in.</summary>
    public static IReadOnlySet<string> Referenced(string content) =>
        UlidPattern().Matches(content).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The acceptance rule, in one place: every spec the run is implementing
    /// is referenced somewhere, and nothing outside the snapshot is
    /// referenced at all.
    ///
    /// The second half matters as much as the first. A reference to a spec
    /// id from another project — or to one a model invented that happens to
    /// be ULID-shaped — would create a traceability link to something that
    /// does not exist, and `df.specs.reconcile` would report it forever.
    /// </summary>
    public static SpecReferenceResult Check(
        IReadOnlyList<FileWrite> files,
        IReadOnlySet<string> requiredSpecIds,
        IReadOnlySet<string> snapshotSpecIds)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var foreignByFile = new List<string>();

        foreach (var file in files)
        {
            foreach (var id in Referenced(file.Content))
            {
                referenced.Add(id);
                if (!snapshotSpecIds.Contains(id))
                {
                    foreignByFile.Add($"{file.Path} references '{id}', which is not in this run's snapshot");
                }
            }
        }

        var missing = requiredSpecIds.Except(referenced, StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new SpecReferenceResult(missing, foreignByFile, referenced);
    }
}

public sealed record SpecReferenceResult(
    IReadOnlyList<string> MissingSpecIds,
    IReadOnlyList<string> ForeignReferences,
    IReadOnlySet<string> Referenced)
{
    public bool IsValid => MissingSpecIds.Count == 0 && ForeignReferences.Count == 0;

    /// <summary>Phrased for a retry prompt: the model needs to know which ids, not that "references were wrong".</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach (var id in MissingSpecIds)
        {
            problems.Add($"no file references spec id '{id}', which this run is implementing");
        }
        problems.AddRange(ForeignReferences);
        return problems;
    }
}
