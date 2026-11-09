using System.Windows;

namespace WICareerBuilder.Desktop.Viewers;

public partial class PdfViewerWindow : Window
{
    private readonly string _path;
    private readonly int? _pageNumber;

    public PdfViewerWindow(string path, int? pageNumber = null)
    {
        _path = path;
        _pageNumber = pageNumber is > 0 ? pageNumber : null;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Title = Path.GetFileName(path) + " - WICareerBuilder";
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await PdfView.EnsureCoreWebView2Async();
        PdfView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        var uri = new UriBuilder(Uri.UriSchemeFile, string.Empty) { Path = _path }.Uri.AbsoluteUri;
        if (_pageNumber is int page) uri += $"#page={page}";
        PdfView.Source = new Uri(uri);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(560, Math.Max(460, workArea.Width * 0.48));
        MinHeight = Math.Min(360, Math.Max(300, workArea.Height * 0.48));
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Max(MinWidth, Math.Min(820, workArea.Width * 0.76));
        Height = Math.Max(MinHeight, Math.Min(520, workArea.Height * 0.74));
    }
}
