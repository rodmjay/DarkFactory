using System.Text;
using System.Text.Json;
using DarkFactory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DarkFactory.Data;

/// <summary>
/// Turns a validated <see cref="SpecDiffDocument"/> (the wire shape a model
/// emits) into the internal <see cref="SpecDiff"/> the graph applies, and
/// checks the things a JSON Schema cannot.
///
/// Schema validity is necessary and nowhere near sufficient here. A model
/// can emit a perfectly-shaped ULID for a node that does not exist, or one
/// belonging to a different project, or a <c>new:7</c> reference into a
/// diff that creates three nodes. Every one of those passes the schema and
/// then fails at a foreign key — or worse, silently retires somebody else's
/// spec. Referential checking is part of "parsed, never trusted", not an
/// afterthought to it.
/// </summary>
public sealed class SpecDiffTranslator(DarkFactoryDbContext db)
{
    private const string NewPrefix = "new:";

    /// <summary>
    /// Checks every id the diff references. Returns violations in the same
    /// shape as schema errors so both kinds can be fed back into a retry
    /// prompt identically — the model does not need to know which layer
    /// rejected it, only what was wrong.
    /// </summary>
    public async Task<SchemaValidationResult> ValidateReferencesAsync(
        string projectId, SpecDiffDocument document, CancellationToken cancellationToken = default)
    {
        var errors = new List<SchemaValidationError>();

        // Every existing spec_id the diff names, gathered up front so this
        // is one query rather than one per reference.
        var referenced = document.Revises.Select(r => r.SpecId)
            .Concat(document.Retires.Select(r => r.SpecId))
            .Concat(document.EdgeAdds.SelectMany(e => new[] { e.FromSpecId, e.ToSpecId })
                .Where(id => !id.StartsWith(NewPrefix, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var known = referenced.Count == 0
            ? []
            : await db.SpecNodes.AsNoTracking()
                .Where(n => n.ProjectId == projectId && referenced.Contains(n.SpecId))
                .Select(n => n.SpecId)
                .ToListAsync(cancellationToken);

        var knownSet = known.ToHashSet(StringComparer.Ordinal);

        void CheckSpecId(string location, string specId)
        {
            if (!knownSet.Contains(specId))
            {
                errors.Add(new SchemaValidationError(location,
                    $"spec_id '{specId}' does not exist in this project. " +
                    "Only reference spec ids that appear in the context you were given."));
            }
        }

        for (var i = 0; i < document.Revises.Count; i++)
        {
            CheckSpecId($"/revises/{i}/spec_id", document.Revises[i].SpecId);
        }

        for (var i = 0; i < document.Retires.Count; i++)
        {
            CheckSpecId($"/retires/{i}/spec_id", document.Retires[i].SpecId);
        }

        for (var i = 0; i < document.EdgeAdds.Count; i++)
        {
            var edge = document.EdgeAdds[i];
            CheckEndpoint($"/edge_adds/{i}/from_spec_id", edge.FromSpecId);
            CheckEndpoint($"/edge_adds/{i}/to_spec_id", edge.ToSpecId);

            if (string.Equals(edge.FromSpecId, edge.ToSpecId, StringComparison.Ordinal))
            {
                errors.Add(new SchemaValidationError($"/edge_adds/{i}",
                    "an edge cannot point a node at itself"));
            }

            void CheckEndpoint(string location, string reference)
            {
                if (!reference.StartsWith(NewPrefix, StringComparison.Ordinal))
                {
                    CheckSpecId(location, reference);
                    return;
                }

                var index = int.Parse(reference[NewPrefix.Length..]);
                if (index >= document.Creates.Count)
                {
                    errors.Add(new SchemaValidationError(location,
                        $"'{reference}' refers to creates[{index}], but this diff only creates " +
                        $"{document.Creates.Count} node(s)."));
                }
            }
        }

        if (document.EdgeRetires.Count > 0)
        {
            var edgeIds = document.EdgeRetires.Select(e => e.EdgeId).Distinct(StringComparer.Ordinal).ToList();
            var knownEdges = (await db.SpecEdges.AsNoTracking()
                .Where(e => e.ProjectId == projectId && edgeIds.Contains(e.Id) && e.RetiredAt == null)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < document.EdgeRetires.Count; i++)
            {
                var id = document.EdgeRetires[i].EdgeId;
                if (!knownEdges.Contains(id))
                {
                    errors.Add(new SchemaValidationError($"/edge_retires/{i}/edge_id",
                        $"edge '{id}' does not exist in this project, or is already retired."));
                }
            }
        }

        if (document.IsEmpty)
        {
            errors.Add(new SchemaValidationError("(root)",
                "the diff is empty. Propose a diff only when there is an actual change to make; " +
                "otherwise keep talking instead."));
        }

        return errors.Count == 0
            ? SchemaValidationResult.Valid
            : new SchemaValidationResult(false, errors);
    }

    /// <summary>
    /// Maps the wire document onto the internal diff. The factory derives
    /// canonical text and content — a model never gets to choose a node's
    /// content hash, or to hand over a canonical form that disagrees with
    /// the text a human was shown and approved.
    /// </summary>
    public static SpecDiff ToSpecDiff(SpecDiffDocument document) => new(
        Creates: document.Creates
            .Select(c => new SpecDiffNodeCreate(c.Kind, c.Layer, Content(c.Text, c.Rationale), Canonicalize(c.Text)))
            .ToList(),
        Revises: document.Revises
            .Select(r => new SpecDiffNodeRevise(r.SpecId, Content(r.Text, r.Rationale), Canonicalize(r.Text)))
            .ToList(),
        Retires: document.Retires.Select(r => new SpecDiffNodeRetire(r.SpecId)).ToList(),
        EdgeAdds: document.EdgeAdds
            .Select(e => new SpecDiffEdgeAdd(e.FromSpecId, e.ToSpecId, e.Kind))
            .ToList(),
        EdgeRetires: document.EdgeRetires.Select(e => new SpecDiffEdgeRetire(e.EdgeId)).ToList());

    /// <summary>
    /// The canonical form a revision is hashed from (docs/adr/0016). Line
    /// endings normalised, trailing whitespace stripped, blank lines
    /// collapsed — so re-proposing the same rule with different incidental
    /// formatting is correctly recognised as the same content rather than
    /// as a new revision.
    /// </summary>
    public static string Canonicalize(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd());

        var builder = new StringBuilder();
        var lastWasBlank = false;
        foreach (var line in lines)
        {
            var blank = line.Length == 0;
            if (blank && lastWasBlank)
            {
                continue;
            }
            builder.Append(line).Append('\n');
            lastWasBlank = blank;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Rationale is stored beside the text but deliberately not part of the
    /// canonical form: two people can agree on a rule and disagree about
    /// why, and that must not produce two revisions of the same rule.
    /// </summary>
    private static string Content(string text, string? rationale) =>
        JsonSerializer.Serialize(new SpecNodeContent(Canonicalize(text), rationale));

    private sealed record SpecNodeContent(string Text, string? Rationale);
}
