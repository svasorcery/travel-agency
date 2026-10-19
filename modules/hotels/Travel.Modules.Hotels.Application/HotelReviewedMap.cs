using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application;

// A bounded process configuration snapshot, not a catalog importer or mutable registry.
public sealed class HotelReviewedMap(ReviewedHotelPair[] pairs)
{
    public ReviewedHotelPair[] Pairs { get; } = pairs.ToArray();

    public bool IsCurrent(HotelAdmissionBinding binding, DateTimeOffset now)
    {
        if (binding.MappingPair is null)
            return binding.MappingRevision is null;
        var membership = Pairs.SelectMany(p => new[] { p.Left, p.Right }).ToArray();
        if (
            Pairs.Length > 100
            || membership.Distinct().Count() != membership.Length
            || Pairs.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != Pairs.Length
        )
            return false;
        var pair = Pairs.SingleOrDefault(p => p.Id == binding.MappingPair);
        return pair is not null
            && HotelPropertyMatching.ReviewedEvidence(pair)
            && binding.MappingEvidenceDigest == HotelPropertyMatching.MappingDigest(pair)
            && pair.Revision == binding.MappingRevision
            && pair.CheckedAt <= now
            && pair.ValidUntil > now
            && pair.ValidUntil >= binding.ValidUntil
            && (pair.Left.Source == binding.Source || pair.Right.Source == binding.Source)
            && pair.UseScope == binding.Source.Profile;
    }
}
