namespace DarkFactory.Data;

// The shape of a proposed amendment (docs/adr/0017) before it's applied to
// the graph (docs/adr/0016). Internal to the service layer for now — this
// becomes the wire contract behind df.specs.propose / the ADR-0021
// spec_diff rendering component once the front surface exists (step 3c+),
// at which point it moves to DarkFactory.Contracts with a JSON Schema.
//
// Every element carries its own Rationale, because docs/adr/0016 (as
// amended) requires an amendment to record who, when, and *why* per change
// — and "why" is per element, not per amendment. One approval routinely
// creates a node for one reason and retires another for a different one,
// and a single rationale on the amendment would have to pick.

/// <param name="Rationale">
/// Why this change, as the proposer stated it. Deliberately outside
/// <paramref name="CanonicalText"/>: two people can agree on a rule and
/// disagree about the reason for it, and that must not produce two
/// revisions of the same rule (docs/adr/0016).
/// </param>
public sealed record SpecDiffNodeCreate(
    string Kind, string Layer, string ContentJson, string CanonicalText, string? Rationale = null);

/// <param name="Rationale">Why this revision. See <see cref="SpecDiffNodeCreate"/>.</param>
public sealed record SpecDiffNodeRevise(
    string SpecId, string ContentJson, string CanonicalText, string? Rationale = null);

public sealed record SpecDiffNodeRetire(string SpecId, string? Rationale = null);

public sealed record SpecDiffEdgeAdd(
    string FromSpecId, string ToSpecId, string Kind, string? Rationale = null);

public sealed record SpecDiffEdgeRetire(string EdgeId, string? Rationale = null);

public sealed record SpecDiff(
    IReadOnlyList<SpecDiffNodeCreate> Creates,
    IReadOnlyList<SpecDiffNodeRevise> Revises,
    IReadOnlyList<SpecDiffNodeRetire> Retires,
    IReadOnlyList<SpecDiffEdgeAdd> EdgeAdds,
    IReadOnlyList<SpecDiffEdgeRetire> EdgeRetires)
{
    public static SpecDiff Empty { get; } = new([], [], [], [], []);
}
