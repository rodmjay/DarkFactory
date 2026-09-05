using DarkFactory.Core;

namespace DarkFactory.Data.Tests;

/// <summary>
/// docs/adr/0024's rule, and 3d's acceptance condition: every spec the run
/// implements is referenced in the code it produced, and nothing outside
/// the snapshot is referenced at all.
/// </summary>
public sealed class SpecReferenceTests
{
    private const string SpecA = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private const string SpecB = "01BX5ZZKBKACTAV9WEVGEMMVRZ";
    private const string Foreign = "01CQZ0PGCC4KVFPNAJ5R6T9HTM";

    private static IReadOnlySet<string> Set(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void CSharpCarriesAnAttributeAndEverythingElseCarriesAComment()
    {
        Assert.Equal($"[Spec(\"{SpecA}\")]", SpecReferences.Format("src/Billing.cs", SpecA));
        Assert.Equal($"-- df:spec {SpecA}", SpecReferences.Format("db/0001.sql", SpecA));
        Assert.Equal($"# df:spec {SpecA}", SpecReferences.Format("infra/main.tf", SpecA));
        Assert.Equal($"// df:spec {SpecA}", SpecReferences.Format("web/app.ts", SpecA));
    }

    [Fact]
    public void EveryRequiredSpecMustBeReferencedSomewhere()
    {
        var files = new List<FileWrite>
        {
            new("src/Billing.cs", $"[Spec(\"{SpecA}\")]\npublic class Billing {{ }}"),
        };

        var result = SpecReferences.Check(files, Set(SpecA, SpecB), Set(SpecA, SpecB));

        Assert.False(result.IsValid);
        Assert.Equal([SpecB], result.MissingSpecIds);
        Assert.Contains(SpecB, string.Join("\n", result.Problems()), StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencingSomethingOutsideTheSnapshotIsRejected()
    {
        var files = new List<FileWrite>
        {
            new("src/Billing.cs", $"[Spec(\"{SpecA}\")]\n// also see {Foreign}"),
        };

        var result = SpecReferences.Check(files, Set(SpecA), Set(SpecA));

        // A link to a spec that is not in the snapshot points at something
        // that may not exist at all, and reconcile would report it forever.
        Assert.False(result.IsValid);
        Assert.Contains(Foreign, string.Join("\n", result.ForeignReferences), StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencesAreFoundInEitherForm()
    {
        var files = new List<FileWrite>
        {
            new("src/Billing.cs", $"[Spec(\"{SpecA}\")]"),
            new("db/0001.sql", $"-- df:spec {SpecB}"),
        };

        var result = SpecReferences.Check(files, Set(SpecA, SpecB), Set(SpecA, SpecB));

        Assert.True(result.IsValid, string.Join("; ", result.Problems()));
        Assert.Equal(Set(SpecA, SpecB).Order(), result.Referenced.Order());
    }

    [Fact]
    public void AnExtraSnapshotSpecNeedNotBeReferenced()
    {
        // The run implements SpecA; SpecB is in the snapshot because the
        // project has it, not because this run touches it. Referencing it
        // is allowed, and not referencing it is not a failure.
        var files = new List<FileWrite> { new("src/Billing.cs", $"[Spec(\"{SpecA}\")]") };

        Assert.True(SpecReferences.Check(files, Set(SpecA), Set(SpecA, SpecB)).IsValid);
    }
}
