using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Data.Tests;

[TestClass]
public sealed class PrivacyBoundaryRepositoryTests
{
    [TestMethod]
    public async Task PrivateProject_ExternalProcessingIsDeniedAndAudited()
    {
        var (root,repo)=await CreateRepositoryAsync();
        try
        {
            var projectId=await repo.CreateCareerProjectAsync("Private build","Engineering","", "Private", true);
            var decision=await repo.AuthorizeExternalProcessingAsync("Project",projectId,"ExternalAI");
            Assert.IsFalse(decision.Allowed);
            var audit=await repo.GetPrivacyAccessAuditAsync("Project",projectId);
            Assert.AreEqual(1,audit.Count);
            Assert.IsFalse(audit[0].Allowed);
        }
        finally { TryDelete(root); }
    }

    [TestMethod]
    public async Task ExplicitWebAllowedProject_CanPassBoundaryAndIsAudited()
    {
        var (root,repo)=await CreateRepositoryAsync();
        try
        {
            var projectId=await repo.CreateCareerProjectAsync("Research build","Engineering","", "WebAllowed", false);
            var decision=await repo.AuthorizeExternalProcessingAsync("Project",projectId,"WebResearch");
            Assert.IsTrue(decision.Allowed);
            var audit=await repo.GetPrivacyAccessAuditAsync("Project",projectId);
            Assert.IsTrue(audit.Single().Allowed);
        }
        finally { TryDelete(root); }
    }

    [TestMethod]
    public async Task Portfolio_InheritsProjectPrivacyAndNoWebBoundary()
    {
        var (root,repo)=await CreateRepositoryAsync();
        try
        {
            var projectId=await repo.CreateCareerProjectAsync("Private portfolio build","Engineering","", "Private", true);
            var portfolioId=await repo.CreateCareerPortfolioEntryAsync(projectId,"Build story","Problem","Result");
            var portfolio=(await repo.GetCareerPortfolioEntriesAsync()).Single(x=>x.Id==portfolioId);
            Assert.IsTrue(portfolio.NoWebAccess);
            Assert.AreEqual("Private",portfolio.PrivacyMode);
            var decision=await repo.AuthorizeExternalProcessingAsync("Portfolio",portfolioId,"ExternalUpload");
            Assert.IsFalse(decision.Allowed);
        }
        finally { TryDelete(root); }
    }

    private static async Task<(string Root,LibraryRepository Repo)> CreateRepositoryAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"WICareerBuilder.Tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database=new DatabaseService(Path.Combine(root,"careerbuilder.db"));
        await database.InitializeAsync();
        return (root,new LibraryRepository(database));
    }

    private static void TryDelete(string root){try{Directory.Delete(root,true);}catch{}}
}
