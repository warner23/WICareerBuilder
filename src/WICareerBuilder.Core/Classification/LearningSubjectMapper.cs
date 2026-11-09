namespace WICareerBuilder.Core.Classification;

/// <summary>Canonical mapping from analyser domain labels to CareerBuilder learning subjects.</summary>
public static class LearningSubjectMapper
{
    public static string Map(string topic) => topic switch
    {
        "Mathematics" => "Engineering Mathematics",
        "Electrical" => "Electrical Principles",
        "Communications / RF" => "RF & Communications",
        "Control / PLC" => "PLC & Control",
        "Measurement & Testing" => "Test & Measurement",
        "General Engineering / Reference" => "",
        "General Engineering" => "",
        _ => topic ?? string.Empty
    };
}
