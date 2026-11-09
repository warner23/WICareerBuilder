using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Data.Tests;

[TestClass]
public sealed class CareerEvidenceRepositoryTests
{
    [TestMethod]
    public async Task Migration0022_AdvancesFormatVersion()
    {
        await WithRepositoryAsync(async (database, _) =>
        {
            await using var connection = await database.OpenConnectionAsync();
            var c = connection.CreateCommand();c.CommandText="SELECT setting_value FROM settings WHERE setting_key='data_format_version';";
            var versionValue = Convert.ToString(await c.ExecuteScalarAsync());
            Assert.IsTrue(int.TryParse(versionValue, out var dataFormatVersion) && dataFormatVersion >= 22,
                $"Migration 0022 requires data format version >= 22; actual value was {versionValue ?? "<null>"}.");
        });
    }

    [TestMethod]
    public async Task DemonstratedProjectEvidence_IsRecommendedButNotUserApprovedAutomatically()
    {
        await WithRepositoryAsync(async (_, repository) =>
        {
            var projectId=await repository.CreateCareerProjectAsync("Motor controller","Engineering","Tested build","Private",true);
            var evidenceId=await repository.CreateCareerEvidenceAsync("Bench verification","ProjectTest","DemonstratedProject","Controlled test",projectId,null,true,true,"Reviewed","Private",true);
            var skillId=await repository.CreateCareerSkillAsync("DC motor control","Electronics","Design and verify motor control systems");
            await repository.LinkEvidenceSkillAsync(evidenceId,skillId,"Strong","Bench test and explanation");
            var evidence=(await repository.GetCareerEvidenceAsync()).Single(x=>x.Id==evidenceId);
            var skill=(await repository.GetCareerSkillsAsync()).Single(x=>x.Id==skillId);
            Assert.IsTrue(evidence.CvReadyRecommended);
            Assert.IsFalse(evidence.CvReadyUserApproved);
            Assert.AreEqual(1,skill.EvidenceCount);
        });
    }

    private static async Task WithRepositoryAsync(Func<DatabaseService,LibraryRepository,Task> body)
    {
        var root=Path.Combine(Path.GetTempPath(),"WICareerBuilder.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{var database=new DatabaseService(Path.Combine(root,"careerbuilder.db"));await database.InitializeAsync();await body(database,new LibraryRepository(database));}
        finally{try{Directory.Delete(root,true);}catch{}}
    }
}
