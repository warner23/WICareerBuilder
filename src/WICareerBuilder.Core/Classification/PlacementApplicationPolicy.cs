using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Core.Classification;

public enum PlacementApplicationAction
{
    NeedsReview,
    AlreadyPlaced,
    ProtectedExisting,
    ApplyPartial,
    ApplyFull
}

public sealed record PlacementApplicationDecision(
    PlacementApplicationAction Action,
    string Disposition,
    string ProposalStatus,
    bool PriorContentPlacement,
    bool HasProtectedPlacement,
    bool SameAppliedPlacement,
    bool WithdrawPriorAutomatic)
{
    public bool ShouldWriteOrganisation
        => Action is PlacementApplicationAction.ApplyPartial or PlacementApplicationAction.ApplyFull;
}

/// <summary>
/// Pure policy for deciding how a content-derived placement may interact with an
/// existing virtual organisation. M03 preserves the v0.9.2 WebBridge behaviour:
/// automatic ContentAnalysis placements may be re-evaluated, while manual/legacy
/// organisation remains protected from automatic overwrite.
/// </summary>
public static class PlacementApplicationPolicy
{
    public static PlacementApplicationDecision Decide(
        ContentPlacementDecision placementDecision,
        ResourceOrganisation? current,
        string provenanceSource)
    {
        var hasAnyPlacement = current is not null
            && (!string.IsNullOrWhiteSpace(current.LearningSubject)
                || !string.IsNullOrWhiteSpace(current.Topic)
                || !string.IsNullOrWhiteSpace(current.Subtopic)
                || !current.StudyStatus.Equals("Unsorted", StringComparison.OrdinalIgnoreCase));

        var priorContentPlacement = hasAnyPlacement
            && (provenanceSource ?? string.Empty).StartsWith("ContentAnalysis", StringComparison.OrdinalIgnoreCase);
        var hasProtectedPlacement = hasAnyPlacement && !priorContentPlacement;

        var sameAppliedPlacement = current is not null
            && current.LearningSubject.Equals(placementDecision.AppliedSubject, StringComparison.OrdinalIgnoreCase)
            && current.Topic.Equals(placementDecision.AppliedTopic, StringComparison.OrdinalIgnoreCase)
            && current.Subtopic.Equals(placementDecision.AppliedSubtopic, StringComparison.OrdinalIgnoreCase);

        if (!placementDecision.CanApplySubject || !placementDecision.CanApplyTopic)
        {
            return new PlacementApplicationDecision(
                PlacementApplicationAction.NeedsReview,
                "NeedsReview",
                "NeedsReview",
                priorContentPlacement,
                hasProtectedPlacement,
                sameAppliedPlacement,
                priorContentPlacement);
        }

        if (sameAppliedPlacement && !priorContentPlacement)
        {
            return new PlacementApplicationDecision(
                PlacementApplicationAction.AlreadyPlaced,
                "AlreadyPlaced",
                placementDecision.IsPartial ? "NeedsReview" : "Applied",
                priorContentPlacement,
                hasProtectedPlacement,
                sameAppliedPlacement,
                false);
        }

        if (hasProtectedPlacement)
        {
            return new PlacementApplicationDecision(
                PlacementApplicationAction.ProtectedExisting,
                "ProtectedExisting",
                "ProtectedExisting",
                priorContentPlacement,
                hasProtectedPlacement,
                sameAppliedPlacement,
                false);
        }

        if (placementDecision.IsPartial)
        {
            return new PlacementApplicationDecision(
                PlacementApplicationAction.ApplyPartial,
                "PartiallyOrganised",
                "NeedsReview",
                priorContentPlacement,
                hasProtectedPlacement,
                sameAppliedPlacement,
                false);
        }

        return new PlacementApplicationDecision(
            PlacementApplicationAction.ApplyFull,
            sameAppliedPlacement ? "AlreadyPlaced" : "AutoOrganised",
            "Applied",
            priorContentPlacement,
            hasProtectedPlacement,
            sameAppliedPlacement,
            false);
    }
}
