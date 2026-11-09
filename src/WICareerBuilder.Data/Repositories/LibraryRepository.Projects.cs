using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<IReadOnlyList<CareerProjectSummary>> GetCareerProjectsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id,p.name,p.category,p.summary,p.status,p.privacy_mode,p.no_web_access,p.cv_eligible,p.portfolio_eligible,
                   (SELECT COUNT(*) FROM career_project_resources pr WHERE pr.project_id=p.id) resource_count,
                   (SELECT COUNT(*) FROM career_project_journal pj WHERE pj.project_id=p.id) journal_count,
                   p.updated_utc
            FROM career_projects p
            ORDER BY p.updated_utc DESC, p.name COLLATE NOCASE;
            """;
        var items = new List<CareerProjectSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadProjectSummary(reader));
        return items;
    }

    public async Task<long> CreateCareerProjectAsync(string name, string category, string summary, string privacyMode, bool noWebAccess, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (name.Length == 0) throw new InvalidOperationException("Project name is required.");
        var now = DateTimeOffset.UtcNow.ToString("O");
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_projects(name,category,summary,status,privacy_mode,no_web_access,cv_eligible,portfolio_eligible,created_utc,updated_utc)
            VALUES($name,$category,$summary,'Active',$privacy,$noWeb,0,0,$utc,$utc)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$category", string.IsNullOrWhiteSpace(category) ? "General" : category.Trim());
        command.Parameters.AddWithValue("$summary", summary.Trim());
        command.Parameters.AddWithValue("$privacy", string.IsNullOrWhiteSpace(privacyMode) ? "Private" : privacyMode.Trim());
        command.Parameters.AddWithValue("$noWeb", noWebAccess ? 1 : 0);
        command.Parameters.AddWithValue("$utc", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<CareerProjectDetail?> GetCareerProjectAsync(long projectId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var projectCommand = connection.CreateCommand();
        projectCommand.CommandText = """
            SELECT p.id,p.name,p.category,p.summary,p.status,p.privacy_mode,p.no_web_access,p.cv_eligible,p.portfolio_eligible,
                   (SELECT COUNT(*) FROM career_project_resources pr WHERE pr.project_id=p.id),
                   (SELECT COUNT(*) FROM career_project_journal pj WHERE pj.project_id=p.id),p.updated_utc
            FROM career_projects p WHERE p.id=$id;
            """;
        projectCommand.Parameters.AddWithValue("$id", projectId);
        CareerProjectSummary? project;
        await using (var reader = await projectCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            project = ReadProjectSummary(reader);
        }

        var resources = new List<ProjectResourceLink>();
        var resourceCommand = connection.CreateCommand();
        resourceCommand.CommandText = """
            SELECT r.id,r.file_name,r.relative_path,pr.relationship_kind,pr.notes
            FROM career_project_resources pr JOIN resources r ON r.id=pr.resource_id
            WHERE pr.project_id=$id ORDER BY r.file_name COLLATE NOCASE;
            """;
        resourceCommand.Parameters.AddWithValue("$id", projectId);
        await using (var reader = await resourceCommand.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                resources.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));

        var journal = new List<ProjectJournalEntry>();
        var journalCommand = connection.CreateCommand();
        journalCommand.CommandText = "SELECT id,project_id,entry_type,title,body,is_private,created_utc FROM career_project_journal WHERE project_id=$id ORDER BY created_utc DESC,id DESC;";
        journalCommand.Parameters.AddWithValue("$id", projectId);
        await using (var reader = await journalCommand.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                journal.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5) != 0, DateTimeOffset.Parse(reader.GetString(6))));
        return new(project, resources, journal);
    }

    public async Task LinkProjectResourceAsync(long projectId, long resourceId, string relationshipKind, string notes, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_project_resources(project_id,resource_id,relationship_kind,notes,created_utc)
            VALUES($project,$resource,$kind,$notes,$utc)
            ON CONFLICT(project_id,resource_id) DO UPDATE SET relationship_kind=excluded.relationship_kind,notes=excluded.notes;
            """;
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$resource", resourceId);
        command.Parameters.AddWithValue("$kind", string.IsNullOrWhiteSpace(relationshipKind) ? "ProjectMaterial" : relationshipKind.Trim());
        command.Parameters.AddWithValue("$notes", notes.Trim());
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long> AddProjectJournalEntryAsync(long projectId, string entryType, string title, string body, bool isPrivate, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO career_project_journal(project_id,entry_type,title,body,is_private,created_utc) VALUES($project,$type,$title,$body,$private,$utc) RETURNING id;";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$type", string.IsNullOrWhiteSpace(entryType) ? "Note" : entryType.Trim());
        command.Parameters.AddWithValue("$title", title.Trim());
        command.Parameters.AddWithValue("$body", body.Trim());
        command.Parameters.AddWithValue("$private", isPrivate ? 1 : 0);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static CareerProjectSummary ReadProjectSummary(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
        reader.GetInt64(6) != 0, reader.GetInt64(7) != 0, reader.GetInt64(8) != 0, reader.GetInt32(9), reader.GetInt32(10), DateTimeOffset.Parse(reader.GetString(11)));
}
