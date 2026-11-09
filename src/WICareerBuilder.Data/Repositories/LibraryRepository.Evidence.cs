using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<IReadOnlyList<CareerSkill>> GetCareerSkillsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id,s.name,s.category,s.description,s.status,
                   (SELECT COUNT(*) FROM career_evidence_skills es WHERE es.skill_id=s.id),s.updated_utc
            FROM career_skills s ORDER BY s.name COLLATE NOCASE;
            """;
        var items = new List<CareerSkill>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt32(5),DateTimeOffset.Parse(reader.GetString(6))));
        return items;
    }

    public async Task<long> CreateCareerSkillAsync(string name, string category, string description, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Skill name is required.");
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_skills(name,category,description,status,created_utc,updated_utc)
            VALUES($name,$category,$description,'Active',$utc,$utc)
            ON CONFLICT(name) DO UPDATE SET category=excluded.category,description=excluded.description,updated_utc=excluded.updated_utc
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(category) ? "General" : category.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$utc", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<CareerEvidenceItem>> GetCareerEvidenceAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,title,evidence_kind,evidence_stage,description,project_id,resource_id,practical_evidence,understanding_demonstrated,
                   verification_status,cv_ready_recommended,cv_ready_user_approved,privacy_mode,no_web_access,updated_utc
            FROM career_evidence ORDER BY updated_utc DESC,id DESC;
            """;
        var items = new List<CareerEvidenceItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadEvidence(reader));
        return items;
    }

    public async Task<long> CreateCareerEvidenceAsync(
        string title, string evidenceKind, string evidenceStage, string description, long? projectId, long? resourceId,
        bool practicalEvidence, bool understandingDemonstrated, string verificationStatus, string privacyMode, bool noWebAccess,
        CancellationToken cancellationToken = default)
    {
        title = title.Trim();
        if (title.Length == 0) throw new InvalidOperationException("Evidence title is required.");
        var readiness = EvidenceReadinessPolicy.Assess(new(evidenceKind,evidenceStage,practicalEvidence,understandingDemonstrated,verificationStatus));
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_evidence(title,evidence_kind,evidence_stage,description,project_id,resource_id,practical_evidence,understanding_demonstrated,
                verification_status,cv_ready_recommended,cv_ready_user_approved,privacy_mode,no_web_access,created_utc,updated_utc)
            VALUES($title,$kind,$stage,$description,$project,$resource,$practical,$understanding,$verification,$recommended,0,$privacy,$noWeb,$utc,$utc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$kind", string.IsNullOrWhiteSpace(evidenceKind) ? "Document" : evidenceKind.Trim());
        command.Parameters.AddWithValue("$stage", string.IsNullOrWhiteSpace(evidenceStage) ? "Mentioned" : evidenceStage.Trim());
        command.Parameters.AddWithValue("$description", description.Trim());
        command.Parameters.AddWithValue("$project", (object?)projectId ?? DBNull.Value);
        command.Parameters.AddWithValue("$resource", (object?)resourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("$practical", practicalEvidence ? 1 : 0);
        command.Parameters.AddWithValue("$understanding", understandingDemonstrated ? 1 : 0);
        command.Parameters.AddWithValue("$verification", string.IsNullOrWhiteSpace(verificationStatus) ? "Unverified" : verificationStatus.Trim());
        command.Parameters.AddWithValue("$recommended", readiness.CanRecommendForCv ? 1 : 0);
        command.Parameters.AddWithValue("$privacy", string.IsNullOrWhiteSpace(privacyMode) ? "Private" : privacyMode.Trim());
        command.Parameters.AddWithValue("$noWeb", noWebAccess ? 1 : 0);
        command.Parameters.AddWithValue("$utc", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task LinkEvidenceSkillAsync(long evidenceId, long skillId, string strength, string notes, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_evidence_skills(evidence_id,skill_id,strength,notes,created_utc)
            VALUES($evidence,$skill,$strength,$notes,$utc)
            ON CONFLICT(evidence_id,skill_id) DO UPDATE SET strength=excluded.strength,notes=excluded.notes;
            """;
        command.Parameters.AddWithValue("$evidence", evidenceId);
        command.Parameters.AddWithValue("$skill", skillId);
        command.Parameters.AddWithValue("$strength", string.IsNullOrWhiteSpace(strength) ? "Supporting" : strength.Trim());
        command.Parameters.AddWithValue("$notes", notes.Trim());
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CareerEvidenceItem ReadEvidence(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
        reader.IsDBNull(5)?null:reader.GetInt64(5),reader.IsDBNull(6)?null:reader.GetInt64(6),reader.GetInt64(7)!=0,reader.GetInt64(8)!=0,
        reader.GetString(9),reader.GetInt64(10)!=0,reader.GetInt64(11)!=0,reader.GetString(12),reader.GetInt64(13)!=0,DateTimeOffset.Parse(reader.GetString(14)));
}
