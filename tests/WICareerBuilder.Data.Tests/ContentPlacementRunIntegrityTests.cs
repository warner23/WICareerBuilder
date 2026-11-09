using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;

namespace WICareerBuilder.Data.Tests;

[TestClass]
public sealed class ContentPlacementRunIntegrityTests
{
    [TestMethod]
    public async Task CompletedRun_PersistsPartialCount_IndependentlyOfReturnedItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "WICareerBuilder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "careerbuilder.db");
        try
        {
            var database = new DatabaseService(databasePath);
            await database.InitializeAsync();
            var repository = new LibraryRepository(database);

            var runId = await repository.StartContentPlacementRunAsync(null, "dc motor", 3);
            await repository.CompleteContentPlacementRunAsync(
                runId, examined: 3, fingerprinted: 1, analysed: 2, cached: 1,
                autoOrganised: 1, partiallyOrganised: 2, needsReview: 2,
                protectedExisting: 0, limited: 0, failed: 0);

            var latest = await repository.GetLatestContentPlacementRunAsync();
            Assert.IsNotNull(latest);
            Assert.AreEqual(2, latest!.PartiallyOrganised,
                "Run-level partial count must come from the persisted run snapshot, not the limited item preview.");
            Assert.AreEqual(0, latest.Items.Count,
                "This test intentionally stores no item rows so a derived item count would be wrong.");

            await using var connection = await database.OpenConnectionAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT partially_organised FROM content_placement_runs WHERE id=$id;";
            command.Parameters.AddWithValue("$id", runId);
            Assert.AreEqual(2L, (long)(await command.ExecuteScalarAsync() ?? -1L));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [TestMethod]
    public async Task Migration0020_RemainsSatisfiedWhenLaterMigrationsAdvanceDataFormatVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "WICareerBuilder.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = new DatabaseService(Path.Combine(root, "careerbuilder.db"));
            await database.InitializeAsync();
            await using var connection = await database.OpenConnectionAsync();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT setting_value FROM settings WHERE setting_key='data_format_version';";
            var value = Convert.ToString(await command.ExecuteScalarAsync());
            Assert.IsTrue(int.TryParse(value, out var version) && version >= 20,
                $"Migration 0020 requires data format version >= 20; actual value was {value ?? "<null>"}.");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
