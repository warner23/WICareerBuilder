using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task SetEvidenceCvApprovalAsync(long evidenceId,bool approved,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var check=connection.CreateCommand();check.CommandText="SELECT cv_ready_recommended FROM career_evidence WHERE id=$id;";check.Parameters.AddWithValue("$id",evidenceId);
        var value=await check.ExecuteScalarAsync(cancellationToken);if(value is null)throw new InvalidOperationException("Evidence not found.");
        var recommended=Convert.ToInt64(value)!=0;
        if(approved&&!recommended)throw new InvalidOperationException("Evidence cannot be approved for CV use until CareerBuilder can recommend it from the evidence rules.");
        var command=connection.CreateCommand();command.CommandText="UPDATE career_evidence SET cv_ready_user_approved=$approved,updated_utc=$utc WHERE id=$id;";command.Parameters.AddWithValue("$approved",approved?1:0);command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));command.Parameters.AddWithValue("$id",evidenceId);await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetProjectCareerEligibilityAsync(long projectId,bool cvEligible,bool portfolioEligible,CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);var command=connection.CreateCommand();command.CommandText="UPDATE career_projects SET cv_eligible=$cv,portfolio_eligible=$portfolio,updated_utc=$utc WHERE id=$id;";command.Parameters.AddWithValue("$cv",cvEligible?1:0);command.Parameters.AddWithValue("$portfolio",portfolioEligible?1:0);command.Parameters.AddWithValue("$utc",DateTimeOffset.UtcNow.ToString("O"));command.Parameters.AddWithValue("$id",projectId);if(await command.ExecuteNonQueryAsync(cancellationToken)==0)throw new InvalidOperationException("Project not found.");
    }

    public async Task<IReadOnlyList<CareerPortfolioEntry>> GetCareerPortfolioEntriesAsync(CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);var command=connection.CreateCommand();command.CommandText="""
            SELECT p.id,p.project_id,cp.name,p.title,p.problem,p.design_summary,p.build_summary,p.test_summary,p.troubleshooting_summary,p.result_summary,p.status,p.privacy_mode,p.no_web_access,p.user_approved_export,
                   (SELECT COUNT(*) FROM career_portfolio_evidence pe WHERE pe.portfolio_id=p.id),p.updated_utc
            FROM career_portfolio_entries p JOIN career_projects cp ON cp.id=p.project_id ORDER BY p.updated_utc DESC,p.id DESC;
            """;var items=new List<CareerPortfolioEntry>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))items.Add(new(reader.GetInt64(0),reader.GetInt64(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetInt64(12)!=0,reader.GetInt64(13)!=0,reader.GetInt32(14),DateTimeOffset.Parse(reader.GetString(15))));return items;
    }

    public async Task<long> CreateCareerPortfolioEntryAsync(long projectId,string title,string problem,string resultSummary,CancellationToken cancellationToken=default)
    {
        title=title.Trim();if(title.Length==0)throw new InvalidOperationException("Portfolio title is required.");
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var project=connection.CreateCommand();project.CommandText="SELECT portfolio_eligible,privacy_mode,no_web_access FROM career_projects WHERE id=$id;";project.Parameters.AddWithValue("$id",projectId);
        string privacyMode; bool noWebAccess;
        await using(var reader=await project.ExecuteReaderAsync(cancellationToken)){if(!await reader.ReadAsync(cancellationToken))throw new InvalidOperationException("Project not found.");privacyMode=reader.GetString(1);noWebAccess=reader.GetInt64(2)!=0;}
        var now=DateTimeOffset.UtcNow.ToString("O");var command=connection.CreateCommand();command.CommandText="""
            INSERT INTO career_portfolio_entries(project_id,title,problem,result_summary,status,privacy_mode,no_web_access,user_approved_export,created_utc,updated_utc)
            VALUES($project,$title,$problem,$result,'Draft',$privacy,$noWeb,0,$utc,$utc) RETURNING id;
            """;command.Parameters.AddWithValue("$project",projectId);command.Parameters.AddWithValue("$title",title);command.Parameters.AddWithValue("$problem",problem.Trim());command.Parameters.AddWithValue("$result",resultSummary.Trim());command.Parameters.AddWithValue("$privacy",privacyMode);command.Parameters.AddWithValue("$noWeb",noWebAccess?1:0);command.Parameters.AddWithValue("$utc",now);return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<CareerWorkspaceSummary> GetCareerWorkspaceSummaryAsync(CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        async Task<int> Count(string sql){var c=connection.CreateCommand();c.CommandText=sql;return Convert.ToInt32(await c.ExecuteScalarAsync(cancellationToken));}
        return new(await Count("SELECT COUNT(*) FROM career_projects;"),await Count("SELECT COUNT(*) FROM career_skills;"),await Count("SELECT COUNT(*) FROM career_evidence;"),await Count("SELECT COUNT(*) FROM career_evidence WHERE cv_ready_recommended=1;"),await Count("SELECT COUNT(*) FROM career_evidence WHERE cv_ready_user_approved=1;"),await Count("SELECT COUNT(*) FROM career_credentials WHERE credential_type='Qualification';"),await Count("SELECT COUNT(*) FROM career_credentials WHERE credential_type='Certification';"),await Count("SELECT COUNT(*) FROM career_cvs;"),await Count("SELECT COUNT(*) FROM career_portfolio_entries;"));
    }
}
