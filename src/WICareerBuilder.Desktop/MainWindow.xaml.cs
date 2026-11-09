using System.Windows;
using Microsoft.Web.WebView2.Core;
using WICareerBuilder.Core.Discovery;
using WICareerBuilder.Data.Repositories;
using WICareerBuilder.Desktop.Infrastructure;
using WICareerBuilder.Desktop.Interop;
using WICareerBuilder.Documents.Analysis;
using WICareerBuilder.Learning.Activities;

namespace WICareerBuilder.Desktop;

public partial class MainWindow : Window
{
    private readonly WebBridge _bridge;
    private readonly string? _captureDirectory;
    private readonly bool _demoTour;

    public MainWindow(
        LibraryRepository repository,
        LearningRepository learningRepository,
        ResourceDiscoveryService discovery,
        ContentFingerprintService fingerprintService,
        PdfResourceAnalyser pdfAnalyser,
        StructuredResourceAnalyser structuredAnalyser,
        LearningActivityEvaluator learningEvaluator,
        UpdateService updateService,
        string? captureDirectory = null,
        bool demoTour = false)
    {
        InitializeComponent();
        _captureDirectory = string.IsNullOrWhiteSpace(captureDirectory) ? null : Path.GetFullPath(captureDirectory);
        _demoTour = demoTour;
        _bridge = new WebBridge(Browser, repository, learningRepository, discovery, fingerprintService, pdfAnalyser, structuredAnalyser, learningEvaluator, updateService, this);
        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(620, Math.Max(480, workArea.Width * 0.55));
        MinHeight = Math.Min(400, Math.Max(320, workArea.Height * 0.55));
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;

        Width = Math.Max(MinWidth, Math.Min(1180, workArea.Width * 0.86));
        Height = Math.Max(MinHeight, Math.Min(760, workArea.Height * 0.84));
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _bridge.InitializeAsync();
        if (_captureDirectory is not null)
        {
            await CaptureProductScreenshotsAsync(_captureDirectory);
            Application.Current.Shutdown(0);
            return;
        }
        if (_demoTour)
            await RunDemoTourAsync();
    }

    private async Task WaitForUiAsync(int milliseconds = 1400)
    {
        await Task.Delay(milliseconds);
    }

    private async Task WaitForUiReadyAsync()
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var ready = await Browser.ExecuteScriptAsync("Boolean(document.readyState === 'complete' && window.WICBProductCapture)");
            if (string.Equals(ready, "true", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(250);
        }
        throw new InvalidOperationException("WICareerBuilder product UI did not become ready for media capture.");
    }

    private async Task ShowProductViewAsync(string view)
    {
        await Browser.ExecuteScriptAsync($"window.WICBProductCapture.showView('{view}')");
        await WaitForUiAsync();
    }

    private async Task CaptureProductScreenshotsAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        Width = Math.Min(MaxWidth, 1440);
        Height = Math.Min(MaxHeight, 900);
        await WaitForUiReadyAsync();
        await WaitForUiAsync(900);

        var shots = new (string View, string File)[]
        {
            ("home", "01-dashboard.png"),
            ("projects", "02-projects.png"),
            ("skills", "03-skills.png"),
            ("evidence", "04-evidence.png"),
            ("cvs", "05-cvs.png"),
            ("portfolio", "06-portfolio.png"),
            ("about", "07-about.png")
        };

        foreach (var shot in shots)
        {
            await ShowProductViewAsync(shot.View);
            await using var stream = File.Create(Path.Combine(directory, shot.File));
            await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        }

        File.WriteAllText(Path.Combine(directory, "CAPTURE_COMPLETE.txt"),
            $"WICareerBuilder product screenshot capture completed {DateTimeOffset.Now:O}{Environment.NewLine}Mode=DEMO; Data={AppPaths.Root}{Environment.NewLine}");
    }

    private async Task RunDemoTourAsync()
    {
        var views = new[] { "home", "projects", "evidence", "skills", "cvs", "portfolio", "about" };
        await WaitForUiReadyAsync();
        await WaitForUiAsync(900);
        foreach (var view in views)
        {
            await ShowProductViewAsync(view);
            await Task.Delay(6500);
        }
        await ShowProductViewAsync("home");
    }
}
