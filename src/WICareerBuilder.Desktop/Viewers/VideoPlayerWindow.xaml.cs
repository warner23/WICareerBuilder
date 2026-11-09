using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace WICareerBuilder.Desktop.Viewers;

public partial class VideoPlayerWindow : Window
{
    private readonly string _path;
    private readonly DispatcherTimer _timer;
    private bool _dragging;
    private readonly int? _startSeconds;

    public VideoPlayerWindow(string path, int? startSeconds = null)
    {
        _path = path;
        _startSeconds = startSeconds is >= 0 ? startSeconds : null;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Title = Path.GetFileName(path) + " - WICareerBuilder";
        Player.Source = new Uri(path, UriKind.Absolute);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => RefreshPosition();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); Player.Close(); };
    }

    private void OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
            Position.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
        if (_startSeconds is int seconds)
            Player.Position = TimeSpan.FromSeconds(Math.Min(seconds, Position.Maximum));
        Player.Play();
    }

    private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Windows could not play this video format internally. You can still use 'Open externally'.\n\n" + e.ErrorException.Message,
            "Video playback", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnPlay(object sender, RoutedEventArgs e) => Player.Play();
    private void OnPause(object sender, RoutedEventArgs e) => Player.Pause();
    private void OnStop(object sender, RoutedEventArgs e) { Player.Stop(); Position.Value = 0; }
    private void OnOpenExternal(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });

    private void OnPositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _dragging || Math.Abs(Player.Position.TotalSeconds - e.NewValue) < 2) return;
        if (Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            _dragging = true;
            Player.Position = TimeSpan.FromSeconds(e.NewValue);
            _dragging = false;
        }
    }

    private void RefreshPosition()
    {
        if (_dragging) return;
        if (Player.NaturalDuration.HasTimeSpan)
        {
            var duration = Player.NaturalDuration.TimeSpan;
            Position.Maximum = Math.Max(1, duration.TotalSeconds);
            Position.Value = Math.Min(Position.Maximum, Player.Position.TotalSeconds);
            TimeText.Text = $"{Format(Player.Position)} / {Format(duration)}";
        }
    }

    private static string Format(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"mm\:ss");

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(560, Math.Max(460, workArea.Width * 0.48));
        MinHeight = Math.Min(360, Math.Max(300, workArea.Height * 0.48));
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Max(MinWidth, Math.Min(800, workArea.Width * 0.74));
        Height = Math.Max(MinHeight, Math.Min(500, workArea.Height * 0.72));
    }
}
