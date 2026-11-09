using System.Globalization;
using System.Text.RegularExpressions;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Learning.Activities;

public sealed class LearningActivityEvaluator
{
    public LearningAnswerResult Evaluate(LearningBlock block, string learnerAnswer)
    {
        var kind = (block.AnswerKind ?? "None").Trim();
        if (kind.Equals("None", StringComparison.OrdinalIgnoreCase))
            return new LearningAnswerResult(false, "This block does not have a checkable answer.");

        if (string.IsNullOrWhiteSpace(learnerAnswer))
            return new LearningAnswerResult(false, "Enter an answer before checking it.");

        if (kind.Equals("Numeric", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryNumber(learnerAnswer, out var actual) || !TryNumber(block.ExpectedAnswer, out var expected))
                return new LearningAnswerResult(false, "The expected answer for this activity is numeric.");

            var tolerance = Math.Abs(block.NumericTolerance ?? 0.0001d);
            var correct = Math.Abs(actual - expected) <= tolerance;
            return new LearningAnswerResult(correct, correct ? "Correct." : "Not quite. Use the hint or work through the method, then try again.");
        }

        var a = Normalise(learnerAnswer);
        var e = Normalise(block.ExpectedAnswer);
        var matches = string.Equals(a, e, StringComparison.OrdinalIgnoreCase);
        return new LearningAnswerResult(matches, matches ? "Correct." : "Not quite. Re-read the question and try again before revealing the explanation.");
    }

    private static string Normalise(string value)
        => string.Join(" ", value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool TryNumber(string value, out double number)
    {
        // Accept a learner answer such as "3", "3.0 A" or "3,0 A" while keeping
        // the checker independent from any one engineering unit. Unit validation can
        // be added later as a separate activity rule rather than hard-coded here.
        var match = Regex.Match(value, @"[-+]?(?:\d+(?:[.,]\d*)?|[.,]\d+)(?:[eE][-+]?\d+)?");
        if (!match.Success) { number = 0; return false; }
        var cleaned = match.Value.Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
