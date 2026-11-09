using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Desktop.Infrastructure;

public static class DemoDataSeeder
{
    public const string DemoMarker = "WICareerBuilder Product Demo";

    public static async Task SeedAsync(LibraryRepository repository, CancellationToken cancellationToken = default)
    {
        var existingProjects = await repository.GetCareerProjectsAsync(cancellationToken);
        if (existingProjects.Any(x => x.Name.Equals("DC Motor Control Demonstrator", StringComparison.OrdinalIgnoreCase)))
            return;

        var motorProject = await repository.CreateCareerProjectAsync(
            "DC Motor Control Demonstrator", "Electrical Engineering",
            "Designed, built and tested a DC motor control demonstrator with documented troubleshooting and verification.",
            "Private", true, cancellationToken);
        await repository.SetProjectCareerEligibilityAsync(motorProject, true, true, cancellationToken);
        await repository.AddProjectJournalEntryAsync(motorProject, "Test", "Controlled speed-control test",
            "Verified the control path under a bounded test sequence and retained the failed/working observations separately.", true, cancellationToken);

        var securityProject = await repository.CreateCareerProjectAsync(
            "Secure Device Monitor", "Cybersecurity / Software",
            "Local-first monitoring prototype demonstrating evidence capture, trust boundaries and repeatable verification.",
            "Private", true, cancellationToken);
        await repository.SetProjectCareerEligibilityAsync(securityProject, true, true, cancellationToken);

        var medicalProject = await repository.CreateCareerProjectAsync(
            "Medical Sensor Research Workbench", "Electronics / Research",
            "Non-diagnostic sensor research workspace for signal acquisition, testing and evidence-led engineering.",
            "Private", true, cancellationToken);
        await repository.SetProjectCareerEligibilityAsync(medicalProject, false, true, cancellationToken);

        var skillElectronics = await repository.CreateCareerSkillAsync("Electronics prototyping", "Engineering", "Practical circuit build, test and fault-finding.", cancellationToken);
        var skillSoftware = await repository.CreateCareerSkillAsync("C# / .NET engineering", "Software", "Desktop application architecture, testing and release engineering.", cancellationToken);
        var skillSecurity = await repository.CreateCareerSkillAsync("Security evidence design", "Cybersecurity", "Local-first evidence, trust boundaries and audit-aware design.", cancellationToken);

        var motorEvidence = await repository.CreateCareerEvidenceAsync(
            "DC motor speed-control build and test", "Project", "DemonstratedProject",
            "Practical build, controlled test evidence, troubleshooting history and demonstrated understanding.",
            motorProject, null, true, true, "Verified", "Private", true, cancellationToken);
        await repository.LinkEvidenceSkillAsync(motorEvidence, skillElectronics, "Strong", "Practical build + repeatable test evidence.", cancellationToken);
        await repository.SetEvidenceCvApprovalAsync(motorEvidence, true, cancellationToken);

        var softwareEvidence = await repository.CreateCareerEvidenceAsync(
            "WICareerBuilder release engineering", "Project", "DemonstratedProject",
            "Milestone-gated C#/.NET desktop engineering with regression tests, release verification and rollback protection.",
            securityProject, null, true, true, "Verified", "Private", true, cancellationToken);
        await repository.LinkEvidenceSkillAsync(softwareEvidence, skillSoftware, "Strong", "Implementation and release verification.", cancellationToken);
        await repository.LinkEvidenceSkillAsync(softwareEvidence, skillSecurity, "Supporting", "Privacy and evidence-boundary design.", cancellationToken);
        await repository.SetEvidenceCvApprovalAsync(softwareEvidence, true, cancellationToken);

        var cvs = await repository.GetCareerCvsAsync(cancellationToken);
        long engineeringCv = cvs.FirstOrDefault(x => x.Name.Equals("Engineering CV", StringComparison.OrdinalIgnoreCase))?.Id
            ?? await repository.CreateCareerCvAsync("Engineering CV", "Engineering / Electronics Role", "Engineering", cancellationToken);
        long softwareCv = cvs.FirstOrDefault(x => x.Name.Equals("Software Engineering CV", StringComparison.OrdinalIgnoreCase))?.Id
            ?? await repository.CreateCareerCvAsync("Software Engineering CV", "C# / .NET Engineer", "Software", cancellationToken);

        await repository.AddEvidenceToCvAsync(engineeringCv, motorEvidence, "DC Motor Control Project",
            "Designed, built, tested and troubleshot a practical DC motor control demonstrator with retained engineering evidence.", cancellationToken);
        await repository.AddEvidenceToCvAsync(softwareCv, softwareEvidence, "WICareerBuilder",
            "Built and release-verified a local-first C#/.NET career evidence workbench using milestone-gated regression testing and rollback-safe promotion.", cancellationToken);

        var portfolio = await repository.GetCareerPortfolioEntriesAsync(cancellationToken);
        if (!portfolio.Any(x => x.Title.Equals("DC Motor Control — Engineering Evidence", StringComparison.OrdinalIgnoreCase)))
            await repository.CreateCareerPortfolioEntryAsync(motorProject, "DC Motor Control — Engineering Evidence",
                "Create a testable practical motor-control build while preserving failures, decisions and verification evidence.",
                "Working demonstrator with documented testing, troubleshooting and lessons retained as project evidence.", cancellationToken);

        var credentials = await repository.GetCareerCredentialsAsync("Certification", cancellationToken);
        if (!credentials.Any(x => x.Title.Equals("Demonstration External Certification", StringComparison.OrdinalIgnoreCase)))
            await repository.CreateCareerCredentialAsync("Certification", "Demonstration External Certification",
                "Example Technical Institute", "Held", "2026-01-15", "", "UserConfirmed", true, null,
                "DEMO DATA ONLY — illustrates the held-credential truth boundary and is not a real user credential.", cancellationToken);
    }
}
