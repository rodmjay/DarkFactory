namespace DarkFactory.Data;

// The shape of a proposed amendment (docs/adr/0017) before it's applied to
// the graph (docs/adr/0016). Internal to the service layer for now — this
// becomes the wire contract behind df.specs.propose / the ADR-0021
// spec_diff rendering component once the front surface exists (step 3c+),
// at which point it moves to DarkFactory.Contracts with a JSON Schema.

public sealed record SpecDiffNodeCreate(string Kind, string Layer, string ContentJson, string CanonicalText);

public sealed record SpecDiffNodeRevise(string SpecId, string ContentJson, string CanonicalText);

public sealed record SpecDiffNodeRetire(string SpecId);

public sealed record SpecDiffEdgeAdd(string FromSpecId, string ToSpecId, string Kind);

public sealed record SpecDiffEdgeRetire(string EdgeId);

public sealed record SpecDiff(
    IReadOnlyList<SpecDiffNodeCreate> Creates,
    IReadOnlyList<SpecDiffNodeRevise> Revises,
    IReadOnlyList<SpecDiffNodeRetire> Retires,
    IReadOnlyList<SpecDiffEdgeAdd> EdgeAdds,
    IReadOnlyList<SpecDiffEdgeRetire> EdgeRetires)
{
    public static SpecDiff Empty { get; } = new([], [], [], [], []);
}
