using CleanSlate.Core;

namespace CleanSlate.Leftover;

public enum OrphanConfidenceGroup
{
    Confirmed,
    Likely,
    Shared,
}

public static class OrphanGrouper
{
    public static OrphanConfidenceGroup Group(OrphanCandidate candidate) =>
        candidate.Kind switch
        {
            OrphanKind.BrokenUninstallEntry when candidate.Confidence >= 80 => OrphanConfidenceGroup.Confirmed,
            OrphanKind.BrokenUninstallEntry => OrphanConfidenceGroup.Likely,
            OrphanKind.OrphanFolder when candidate.Confidence >= 55 => OrphanConfidenceGroup.Likely,
            _ => OrphanConfidenceGroup.Shared,
        };

    public static ILookup<OrphanConfidenceGroup, OrphanCandidate> GroupAll(IEnumerable<OrphanCandidate> items) =>
        items.ToLookup(Group);
}
