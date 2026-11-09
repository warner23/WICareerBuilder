using System.Windows;
using WICareerBuilder.Core.Discovery;
using WICareerBuilder.Data.Database;
using WICareerBuilder.Data.Repositories;
using WICareerBuilder.Desktop.Infrastructure;
using WICareerBuilder.Documents.Analysis;
using WICareerBuilder.Learning.Activities;

namespace WICareerBuilder.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            AppPaths.ConfigureFromArgs(e.Args);
            if (AppPaths.DemoMode && e.Args.Contains("--reset-demo", StringComparer.OrdinalIgnoreCase) && Directory.Exists(AppPaths.Root))
                Directory.Delete(AppPaths.Root, recursive: true);
            AppPaths.Ensure();
            var database = new DatabaseService(AppPaths.DatabaseFile);
            await database.InitializeAsync();

            if (e.Args.Contains("--init-db", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(0);
                return;
            }

            var repository = new LibraryRepository(database);
            if (AppPaths.DemoMode)
                await DemoDataSeeder.SeedAsync(repository);
            await repository.RefreshResourceRelationshipsAsync();
            var learningRepository = new LearningRepository(database);
            var discovery = new ResourceDiscoveryService();
            var fingerprintService = new ContentFingerprintService();
            var pdfAnalyser = new PdfResourceAnalyser();
            var structuredAnalyser = new StructuredResourceAnalyser();
            var learningEvaluator = new LearningActivityEvaluator();
            var updateService = new UpdateService();
            var captureIndex = Array.FindIndex(e.Args, x => x.Equals("--capture-product-media", StringComparison.OrdinalIgnoreCase));
            string? captureDirectory = captureIndex >= 0 && captureIndex + 1 < e.Args.Length ? e.Args[captureIndex + 1] : null;
            var demoTour = e.Args.Contains("--demo-tour", StringComparer.OrdinalIgnoreCase);
            MainWindow = new MainWindow(repository, learningRepository, discovery, fingerprintService, pdfAnalyser, structuredAnalyser, learningEvaluator, updateService, captureDirectory, demoTour);
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            SimpleLog.Write("Startup failure: " + ex);
            MessageBox.Show(ex.Message, "WICareerBuilder could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
