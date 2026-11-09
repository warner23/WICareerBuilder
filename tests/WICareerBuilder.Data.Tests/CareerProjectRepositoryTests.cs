using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Data.Tests;

[TestClass]
public sealed class CareerProjectRepositoryTests
{
    [TestMethod]
    public async Task Migration0021_AdvancesFormatAndCreatesProjectTables()
    {
        await WithRepositoryAsync(async (database, _) =>
        {
            await using var connection = await database.OpenConnectionAsync();
            var version = connection.CreateCommand();
            version.CommandText = "SELECT setting_value FROM settings WHERE setting_key='data_format_version';";
            var versionValue = Convert.ToString(await version.ExecuteScalarAsync());
            Assert.IsTrue(int.TryParse(versionValue, out var dataFormatVersion) && dataFormatVersion >= 21,
                $"Migration 0021 requires data format version >= 21; actual value was {versionValue ?? "<null>"}.");
            var table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('career_projects','career_project_resources','career_project_journal');";
            Assert.AreEqual(3L, Convert.ToInt64(await table.ExecuteScalarAsync()));
        });
    }

    [TestMethod]
    public async Task ProjectWorkspace_PersistsPrivacyAndPrivateJournal()
    {
        await WithRepositoryAsync(async (_, repository) =>
        {
            var id = await repository.CreateCareerProjectAsync("WIHeart", "Engineering", "ECG research platform", "Private", true);
            await repository.AddProjectJournalEntryAsync(id, "Test", "Detector regression", "Failure retained as engineering evidence.", true);
            var project = await repository.GetCareerProjectAsync(id);
            Assert.IsNotNull(project);
            Assert.IsTrue(project!.Project.NoWebAccess);
            Assert.AreEqual("Private", project.Project.PrivacyMode);
            Assert.AreEqual(1, project.Project.JournalCount);
            Assert.IsTrue(project.Journal.Single().IsPrivate);
        });
    }

    private static async Task WithRepositoryAsync(Func<DatabaseService, LibraryRepository, Task> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "WICareerBuilder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new DatabaseService(Path.Combine(root, "careerbuilder.db"));
            await database.InitializeAsync();
            await body(database, new LibraryRepository(database));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
