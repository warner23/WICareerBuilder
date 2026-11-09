namespace WICareerBuilder.Core.Models;

public sealed record LearningModuleSummary(
    long Id,
    string Title,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string Status,
    long? PrimaryResourceId,
    string? PrimaryResourceName,
    int BlockCount,
    DateTimeOffset UpdatedUtc);

public sealed record LearningModule(
    long Id,
    string Title,
    string Summary,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string Status,
    long? PrimaryResourceId,
    string? PrimaryResourceName,
    IReadOnlyList<LearningBlock> Blocks,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record LearningBlock(
    long Id,
    long ModuleId,
    string BlockType,
    int SortOrder,
    string Title,
    string Body,
    string Formula,
    string Prompt,
    string AnswerKind,
    string ExpectedAnswer,
    double? NumericTolerance,
    string Hint,
    string Explanation,
    IReadOnlyList<LearningSourceReference> Sources);

public sealed record LearningSourceReference(
    long Id,
    long BlockId,
    long ResourceId,
    string ResourceName,
    int? PageNumber,
    int? EndPageNumber,
    int? TimestampSeconds,
    string Note);

public sealed record LearningModuleDraft(
    string Title,
    string Summary,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    long? PrimaryResourceId);

public sealed record LearningBlockDraft(
    long ModuleId,
    string BlockType,
    string Title,
    string Body,
    string Formula,
    string Prompt,
    string AnswerKind,
    string ExpectedAnswer,
    double? NumericTolerance,
    string Hint,
    string Explanation,
    long? SourceResourceId,
    int? SourcePageNumber,
    int? SourceEndPageNumber,
    int? SourceTimestampSeconds,
    string SourceNote);

public sealed record LearningAnswerResult(bool IsCorrect, string Message);
