using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Data.Tests;

[TestClass]
public sealed class CareerJourneyEndToEndTests
{
    [TestMethod]
    public async Task DemonstratedProjectEvidence_RequiresApprovalBeforeCv_AndKeepsProvenance()
    {
        var root=Path.Combine(Path.GetTempPath(),"WICareerBuilder.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var database=new DatabaseService(Path.Combine(root,"careerbuilder.db"));await database.InitializeAsync();var repo=new LibraryRepository(database);
            var projectId=await repo.CreateCareerProjectAsync("DC Motor Controller","Engineering","Bench-tested speed control build","Private",true);
            var skillId=await repo.CreateCareerSkillAsync("DC motor control","Electrical Engineering","Design, build and test DC motor control circuits");
            var evidenceId=await repo.CreateCareerEvidenceAsync("Bench-tested DC motor controller","ProjectTest","DemonstratedProject","Built and tested a motor control circuit",projectId,null,true,true,"Reviewed","Private",true);
            await repo.LinkEvidenceSkillAsync(evidenceId,skillId,"Strong","Practical build and controlled test");
            await repo.AddEvidenceProvenanceAsync(evidenceId,"ScannedFixture","DC Motor Speed Control.txt","Electrical Machines","DC Machines","Speed Control","Headless V1 validation fixture");
            var cvId=await repo.CreateCareerCvAsync("Engineering CV","Engineering Technician","Engineering");

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>repo.AddEvidenceToCvAsync(cvId,evidenceId,"Projects","Built and tested a DC motor controller."));
            await repo.SetEvidenceCvApprovalAsync(evidenceId,true);
            await repo.AddEvidenceToCvAsync(cvId,evidenceId,"Projects","Built and tested a DC motor controller.");

            var cvItems=await repo.GetCareerCvItemsAsync(cvId);Assert.AreEqual(1,cvItems.Count);Assert.AreEqual("Evidence",cvItems[0].SourceKind);
            var provenance=await repo.GetEvidenceProvenanceAsync(evidenceId);Assert.AreEqual(1,provenance.Count);Assert.AreEqual("Speed Control",provenance[0].Subtopic);
            var portfolioId=await repo.CreateCareerPortfolioEntryAsync(projectId,"DC motor controller","Control motor speed safely","Bench test passed");
            var portfolio=(await repo.GetCareerPortfolioEntriesAsync()).Single(x=>x.Id==portfolioId);Assert.IsTrue(portfolio.NoWebAccess);Assert.IsFalse(portfolio.UserApprovedExport);
        }
        finally{try{Directory.Delete(root,true);}catch{}}
    }

    [TestMethod]
    public async Task Migration0027_ProvidesEvidenceTraceabilityAndCurrentFormatAtLeastTwentySeven()
    {
        var root=Path.Combine(Path.GetTempPath(),"WICareerBuilder.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var database=new DatabaseService(Path.Combine(root,"careerbuilder.db"));await database.InitializeAsync();await using var connection=await database.OpenConnectionAsync();
            var table=connection.CreateCommand();table.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name='career_evidence_provenance';";Assert.AreEqual("career_evidence_provenance",Convert.ToString(await table.ExecuteScalarAsync()));
            var version=connection.CreateCommand();version.CommandText="SELECT setting_value FROM settings WHERE setting_key='data_format_version';";var value=Convert.ToString(await version.ExecuteScalarAsync());Assert.IsTrue(int.TryParse(value,out var parsed)&&parsed>=27,$"Expected data format >= 27; actual {value}");
        }
        finally{try{Directory.Delete(root,true);}catch{}}
    }
}
