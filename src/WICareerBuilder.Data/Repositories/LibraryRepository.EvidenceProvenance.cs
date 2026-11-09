using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<long> AddEvidenceProvenanceAsync(
        long evidenceId,
        string sourceKind,
        string sourceReference,
        string learningSubject,
        string topic,
        string subtopic,
        string notes,
        CancellationToken cancellationToken = default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var exists=connection.CreateCommand();exists.CommandText="SELECT 1 FROM career_evidence WHERE id=$id;";exists.Parameters.AddWithValue("$id",evidenceId);
        if(await exists.ExecuteScalarAsync(cancellationToken) is null)throw new InvalidOperationException("Evidence not found.");
        var command=connection.CreateCommand();command.CommandText="""
            INSERT INTO career_evidence_provenance(evidence_id,source_kind,source_reference,learning_subject,topic,subtopic,notes,created_utc)
            VALUES($evidence,$kind,$reference,$subject,$topic,$subtopic,$notes,$utc) RETURNING id;
            """;
        command.Parameters.AddWithValue("$evidence",evidenceId);
        command.Parameters.AddWithValue("$kind",string.IsNullOrWhiteSpace(sourceKind)?"Unknown":sourceKind.Trim());
        command.Parameters.AddWithValue("$reference",sourceReference.Trim());
        command.Parameters.AddWithValue("$subject",learningSubject.Trim());
        command.Parameters.AddWithValue("$topic",topic.Trim());
        command.Parameters.AddWithValue("$subtopic",subtopic.Trim());
        command.Parameters.AddWithValue("$notes",notes.Trim());
        command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<CareerEvidenceProvenance>> GetEvidenceProvenanceAsync(long evidenceId,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var command=connection.CreateCommand();command.CommandText="""
            SELECT id,evidence_id,source_kind,source_reference,learning_subject,topic,subtopic,notes,created_utc
            FROM career_evidence_provenance WHERE evidence_id=$id ORDER BY created_utc,id;
            """;command.Parameters.AddWithValue("$id",evidenceId);
        var items=new List<CareerEvidenceProvenance>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),DateTimeOffset.Parse(reader.GetString(8))));
        return items;
    }
}
