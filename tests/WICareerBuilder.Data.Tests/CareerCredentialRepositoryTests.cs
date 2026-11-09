using WICareerBuilder.Data.Database;using WICareerBuilder.Data.Repositories;
namespace WICareerBuilder.Data.Tests;
[TestClass]
public sealed class CareerCredentialRepositoryTests
{
    [TestMethod] public async Task Migration0023_AdvancesFormatVersion(){await WithRepositoryAsync(async(db,_)=>{await using var c=await db.OpenConnectionAsync();var q=c.CreateCommand();q.CommandText="SELECT setting_value FROM settings WHERE setting_key='data_format_version';";var versionValue=Convert.ToString(await q.ExecuteScalarAsync());Assert.IsTrue(int.TryParse(versionValue,out var dataFormatVersion)&&dataFormatVersion>=23,$"Migration 0023 requires data format version >= 23; actual value was {versionValue ?? "<null>"}.");});}
    [TestMethod] public async Task HeldQualification_RejectsMissingUserConfirmation(){await WithRepositoryAsync(async(_,r)=>{await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>r.CreateCareerCredentialAsync("Qualification","ONC Engineering","College","Held","","","Unverified",false,null,""));});}
    private static async Task WithRepositoryAsync(Func<DatabaseService,LibraryRepository,Task> body){var root=Path.Combine(Path.GetTempPath(),"WICareerBuilder.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var db=new DatabaseService(Path.Combine(root,"careerbuilder.db"));await db.InitializeAsync();await body(db,new LibraryRepository(db));}finally{try{Directory.Delete(root,true);}catch{}}}
}
