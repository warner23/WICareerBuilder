namespace WICareerBuilder.Core.Models;

public sealed record ResourceOrganisation(
    long ResourceId,
    string Subject,
    string CourseLevel,
    string LearningSubject,
    string Topic,
    string Subtopic,
    string ResourceType,
    string StudyStatus,
    string Notes,
    DateTimeOffset? UpdatedUtc);
