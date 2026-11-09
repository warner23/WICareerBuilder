namespace WICareerBuilder.Core.Classification;

public sealed record PrivacyBoundaryInput(string PrivacyMode, bool NoWebAccess, string Operation);
public sealed record PrivacyBoundaryDecision(bool Allowed, string Reason);

/// <summary>
/// Lower-layer privacy boundary for every future external AI/web/remote-processing path.
/// Local indexing, local analysis and local export are outside this gate; external operations
/// require an explicit WebAllowed privacy mode and NoWebAccess=false.
/// </summary>
public static class PrivacyBoundaryPolicy
{
    private static readonly HashSet<string> ExternalOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "WebResearch", "ExternalAI", "ExternalUpload", "RemoteProcessing", "ExternalLinkPreview"
    };

    public static PrivacyBoundaryDecision Assess(PrivacyBoundaryInput input)
    {
        var mode = (input.PrivacyMode ?? "").Trim();
        var operation = (input.Operation ?? "").Trim();

        if (!ExternalOperations.Contains(operation))
            return new(true, "Operation is local and does not cross the external-processing privacy boundary.");

        if (input.NoWebAccess)
            return new(false, "No-web access is enabled for this record; external processing is denied.");

        if (!mode.Equals("WebAllowed", StringComparison.OrdinalIgnoreCase))
            return new(false, "External processing requires explicit WebAllowed privacy mode; Private, Local and portfolio eligibility are not web consent.");

        return new(true, "External processing is explicitly allowed for this record and no-web access is disabled.");
    }
}
