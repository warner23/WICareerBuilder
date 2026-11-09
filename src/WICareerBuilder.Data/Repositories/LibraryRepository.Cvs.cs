using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<MasterEvidenceSummary> GetMasterEvidenceSummaryAsync(CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        async Task<int> CountAsync(string sql){var c=connection.CreateCommand();c.CommandText=sql;return Convert.ToInt32(await c.ExecuteScalarAsync(cancellationToken));}
        return new(
            await CountAsync("SELECT COUNT(*) FROM career_projects;"),
            await CountAsync("SELECT COUNT(*) FROM career_skills;"),
            await CountAsync("SELECT COUNT(*) FROM career_evidence;"),
            await CountAsync("SELECT COUNT(*) FROM career_evidence WHERE cv_ready_recommended=1 AND cv_ready_user_approved=1;"),
            await CountAsync("SELECT COUNT(*) FROM career_credentials;"),
            await CountAsync("SELECT COUNT(*) FROM career_cvs;"));
    }

    public async Task<IReadOnlyList<CareerCvSummary>> GetCareerCvsAsync(CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var command=connection.CreateCommand();command.CommandText="""
            SELECT c.id,c.name,c.target_role,c.focus_key,c.status,(SELECT COUNT(*) FROM career_cv_items i WHERE i.cv_id=c.id),c.updated_utc
            FROM career_cvs c ORDER BY c.updated_utc DESC,c.name COLLATE NOCASE;
            """;
        var items=new List<CareerCvSummary>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt32(5),DateTimeOffset.Parse(reader.GetString(6))));
        return items;
    }

    public async Task<long> CreateCareerCvAsync(string name,string targetRole,string focusKey,CancellationToken cancellationToken=default)
    {
        name=name.Trim();if(name.Length==0)throw new InvalidOperationException("CV name is required.");var now=DateTimeOffset.UtcNow.ToString("O");
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);var c=connection.CreateCommand();c.CommandText="INSERT INTO career_cvs(name,target_role,focus_key,status,created_utc,updated_utc) VALUES($name,$role,$focus,'Draft',$utc,$utc) RETURNING id;";c.Parameters.AddWithValue("$name",name);c.Parameters.AddWithValue("$role",targetRole.Trim());c.Parameters.AddWithValue("$focus",string.IsNullOrWhiteSpace(focusKey)?"General":focusKey.Trim());c.Parameters.AddWithValue("$utc",now);return Convert.ToInt64(await c.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AddEvidenceToCvAsync(long cvId,long evidenceId,string heading,string statement,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var check=connection.CreateCommand();check.CommandText="SELECT cv_ready_recommended,cv_ready_user_approved FROM career_evidence WHERE id=$id;";check.Parameters.AddWithValue("$id",evidenceId);
        bool recommended=false,approved=false;await using(var reader=await check.ExecuteReaderAsync(cancellationToken)){if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("Evidence not found.");recommended=reader.GetInt64(0)!=0;approved=reader.GetInt64(1)!=0;}
        var decision=CvItemEligibilityPolicy.Assess(new("Evidence",recommended,approved,false,""));if(!decision.CanAdd)throw new InvalidOperationException(decision.Reason);
        var c=connection.CreateCommand();c.CommandText="""
            INSERT INTO career_cv_items(cv_id,source_kind,source_id,heading,statement,sort_order,user_approved,created_utc)
            VALUES($cv,'Evidence',$source,$heading,$statement,(SELECT COALESCE(MAX(sort_order),0)+10 FROM career_cv_items WHERE cv_id=$cv),1,$utc)
            ON CONFLICT(cv_id,source_kind,source_id) DO UPDATE SET heading=excluded.heading,statement=excluded.statement,user_approved=1;
            UPDATE career_cvs SET updated_utc=$utc WHERE id=$cv;
            """;c.Parameters.AddWithValue("$cv",cvId);c.Parameters.AddWithValue("$source",evidenceId);c.Parameters.AddWithValue("$heading",heading.Trim());c.Parameters.AddWithValue("$statement",statement.Trim());c.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));await c.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddCredentialToCvAsync(long cvId,long credentialId,string heading,string statement,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var check=connection.CreateCommand();check.CommandText="SELECT user_confirmed,status FROM career_credentials WHERE id=$id;";check.Parameters.AddWithValue("$id",credentialId);
        bool confirmed;string status;await using(var reader=await check.ExecuteReaderAsync(cancellationToken)){if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("Credential not found.");confirmed=reader.GetInt64(0)!=0;status=reader.GetString(1);}
        var decision=CvItemEligibilityPolicy.Assess(new("Credential",false,false,confirmed,status));if(!decision.CanAdd)throw new InvalidOperationException(decision.Reason);
        var c=connection.CreateCommand();c.CommandText="""
            INSERT INTO career_cv_items(cv_id,source_kind,source_id,heading,statement,sort_order,user_approved,created_utc)
            VALUES($cv,'Credential',$source,$heading,$statement,(SELECT COALESCE(MAX(sort_order),0)+10 FROM career_cv_items WHERE cv_id=$cv),1,$utc)
            ON CONFLICT(cv_id,source_kind,source_id) DO UPDATE SET heading=excluded.heading,statement=excluded.statement,user_approved=1;
            UPDATE career_cvs SET updated_utc=$utc WHERE id=$cv;
            """;c.Parameters.AddWithValue("$cv",cvId);c.Parameters.AddWithValue("$source",credentialId);c.Parameters.AddWithValue("$heading",heading.Trim());c.Parameters.AddWithValue("$statement",statement.Trim());c.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));await c.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CareerCvItem>> GetCareerCvItemsAsync(long cvId,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);var c=connection.CreateCommand();c.CommandText="SELECT id,cv_id,source_kind,source_id,heading,statement,sort_order,user_approved FROM career_cv_items WHERE cv_id=$cv ORDER BY sort_order,id;";c.Parameters.AddWithValue("$cv",cvId);var items=new List<CareerCvItem>();await using var reader=await c.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetInt64(3),reader.GetString(4),reader.GetString(5),reader.GetInt32(6),reader.GetInt64(7)!=0));return items;
    }
}
