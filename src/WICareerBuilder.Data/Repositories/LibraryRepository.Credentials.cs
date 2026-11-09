using WICareerBuilder.Core.Classification;
using WICareerBuilder.Core.Models;

namespace WICareerBuilder.Data.Repositories;

public sealed partial class LibraryRepository
{
    public async Task<IReadOnlyList<CareerCredential>> GetCareerCredentialsAsync(string credentialType="",CancellationToken cancellationToken=default)
    {
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var command=connection.CreateCommand();
        command.CommandText="""
            SELECT id,credential_type,title,issuer,status,award_date,expiry_date,verification_status,user_confirmed,evidence_id,notes,updated_utc
            FROM career_credentials
            WHERE ($type='' OR credential_type=$type COLLATE NOCASE)
            ORDER BY updated_utc DESC,id DESC;
            """;
        command.Parameters.AddWithValue("$type",credentialType.Trim());
        var items=new List<CareerCredential>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) items.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetInt64(8)!=0,reader.IsDBNull(9)?null:reader.GetInt64(9),reader.GetString(10),DateTimeOffset.Parse(reader.GetString(11))));
        return items;
    }

    public async Task<long> CreateCareerCredentialAsync(string credentialType,string title,string issuer,string status,string awardDate,string expiryDate,string verificationStatus,bool userConfirmed,long? evidenceId,string notes,CancellationToken cancellationToken=default)
    {
        title=title.Trim();if(title.Length==0)throw new InvalidOperationException("Credential title is required.");
        var decision=CredentialTruthPolicy.Assess(new(credentialType,status,issuer,userConfirmed,verificationStatus));
        if(status.Equals("Held",StringComparison.OrdinalIgnoreCase)&&!decision.CanRecordAsHeld) throw new InvalidOperationException(decision.Reason);
        var now=DateTimeOffset.UtcNow.ToString("O");
        await using var connection=await _database.OpenConnectionAsync(cancellationToken);
        var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO career_credentials(credential_type,title,issuer,status,award_date,expiry_date,verification_status,user_confirmed,evidence_id,notes,created_utc,updated_utc)
            VALUES($type,$title,$issuer,$status,$award,$expiry,$verification,$confirmed,$evidence,$notes,$utc,$utc) RETURNING id;
            """;
        command.Parameters.AddWithValue("$type",credentialType.Trim());command.Parameters.AddWithValue("$title",title);command.Parameters.AddWithValue("$issuer",issuer.Trim());command.Parameters.AddWithValue("$status",status.Trim());command.Parameters.AddWithValue("$award",awardDate.Trim());command.Parameters.AddWithValue("$expiry",expiryDate.Trim());command.Parameters.AddWithValue("$verification",verificationStatus.Trim());command.Parameters.AddWithValue("$confirmed",userConfirmed?1:0);command.Parameters.AddWithValue("$evidence",(object?)evidenceId??DBNull.Value);command.Parameters.AddWithValue("$notes",notes.Trim());command.Parameters.AddWithValue("$utc",now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
