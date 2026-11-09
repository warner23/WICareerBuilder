using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<PrivacyBoundaryDecision> AuthorizeExternalProcessingAsync(
        string sourceKind,
        long sourceId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var (privacyMode, noWebAccess) = await GetPrivacyStateAsync(sourceKind, sourceId, cancellationToken);
        var decision = PrivacyBoundaryPolicy.Assess(new(privacyMode, noWebAccess, operation));

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO career_privacy_access_audit(source_kind,source_id,operation,privacy_mode,no_web_access,allowed,reason,created_utc)
            VALUES($kind,$id,$operation,$privacy,$noWeb,$allowed,$reason,$utc);
            """;
        command.Parameters.AddWithValue("$kind", sourceKind.Trim());
        command.Parameters.AddWithValue("$id", sourceId);
        command.Parameters.AddWithValue("$operation", operation.Trim());
        command.Parameters.AddWithValue("$privacy", privacyMode);
        command.Parameters.AddWithValue("$noWeb", noWebAccess ? 1 : 0);
        command.Parameters.AddWithValue("$allowed", decision.Allowed ? 1 : 0);
        command.Parameters.AddWithValue("$reason", decision.Reason);
        command.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return decision;
    }

    public async Task<IReadOnlyList<CareerPrivacyAccessAudit>> GetPrivacyAccessAuditAsync(
        string sourceKind,
        long sourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,source_kind,source_id,operation,privacy_mode,no_web_access,allowed,reason,created_utc
            FROM career_privacy_access_audit
            WHERE source_kind=$kind AND source_id=$id
            ORDER BY created_utc DESC,id DESC;
            """;
        command.Parameters.AddWithValue("$kind", sourceKind.Trim());
        command.Parameters.AddWithValue("$id", sourceId);
        var items = new List<CareerPrivacyAccessAudit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetInt64(2),reader.GetString(3),reader.GetString(4),reader.GetInt64(5)!=0,reader.GetInt64(6)!=0,reader.GetString(7),DateTimeOffset.Parse(reader.GetString(8))));
        return items;
    }

    private async Task<(string PrivacyMode, bool NoWebAccess)> GetPrivacyStateAsync(
        string sourceKind,
        long sourceId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        switch ((sourceKind ?? "").Trim().ToLowerInvariant())
        {
            case "project":
                command.CommandText = "SELECT privacy_mode,no_web_access FROM career_projects WHERE id=$id;";
                break;
            case "evidence":
                command.CommandText = "SELECT privacy_mode,no_web_access FROM career_evidence WHERE id=$id;";
                break;
            case "portfolio":
                command.CommandText = "SELECT privacy_mode,no_web_access FROM career_portfolio_entries WHERE id=$id;";
                break;
            default:
                throw new InvalidOperationException("Privacy boundary supports Project, Evidence or Portfolio records only.");
        }
        command.Parameters.AddWithValue("$id", sourceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException($"{sourceKind} record not found.");
        return (reader.GetString(0), reader.GetInt64(1) != 0);
    }
}
