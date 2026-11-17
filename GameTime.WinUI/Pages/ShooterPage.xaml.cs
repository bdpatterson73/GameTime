using System.Diagnostics;
using System.Runtime.InteropServices;
using GameTime.WinUI.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace GameTime.WinUI.Pages;

public sealed partial class ShooterPage : Page
{
    private const int RenderWidth = 800, RenderHeight = 480;

    private MazeWorld _world = null!;
    private ShooterBot _bot = null!;
    private readonly RaycastRenderer _renderer = new(RenderWidth, RenderHeight);
    private readonly byte[] _frameBytes = new byte[RenderWidth * RenderHeight * 4];
    private CanvasBitmap? _frame;

    private bool _running;
    private bool _betweenMazes;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastTime;
    private DateTime _mazeStart;

    // Cumulative stats
    private int _mazesCleared;
    private int _totalKills;
    private int _totalShots;
    private TimeSpan _fastestClear = TimeSpan.Zero;

    public ShooterPage()
    {
        InitializeComponent();
        Motion.KeepCentered(ClearBanner);
        NewMaze();
        RenderFrame();

        // Only simulate and draw while the page is on screen.
        Loaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
        };
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Motion.PlayHeaderAnimation(HeaderIcon);
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        base.OnNavigatingFrom(e);
        if (e.NavigationMode == NavigationMode.Back) Motion.PrepareReturnAnimation(HeaderIcon);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (!_running) return;
        _running = false;
        StartStopButton.Content = "▶  Start";
        StatusText.Text = "Paused.";
    }

    private void NewMaze()
    {
        _world = new MazeWorld(size: 21, creatureCount: 10);
        _bot = new ShooterBot(_world);
        _bot.Fired += () => { _totalShots++; };
        _bot.CreatureKilled += _ => { _totalKills++; };
        _mazeStart = DateTime.Now;
        _betweenMazes = false;
        HideBanner();
        UpdateStatsPanel();
    }

    private void OnRendering(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastTime, 0, 0.05);
        _lastTime = now;

        if (_running && !_betweenMazes)
        {
            double speedScale = 0.4 + SpeedSlider.Value * 0.16; // 0.56..2.0
            _world.UpdateCreatures(dt * speedScale);
            _bot.Update(dt, speedScale);

            if (_world.AliveCount == 0)
                _ = OnMazeClearedAsync();
        }

        RenderFrame();
        UpdateStatsPanel();
    }

    private async Task OnMazeClearedAsync()
    {
        _betweenMazes = true;
        _mazesCleared++;
        var elapsed = DateTime.Now - _mazeStart;
        if (_fastestClear == TimeSpan.Zero || elapsed < _fastestClear) _fastestClear = elapsed;
        StatusText.Text = "Maze cleared! Generating a new one…";
        ShowBanner();
        await Task.Delay(1800);
        if (_running) { NewMaze(); StatusText.Text = "Hunting targets…"; }
        else _betweenMazes = false;
    }

    private void RenderFrame()
    {
        _renderer.Render(_world, _bot, _clock.Elapsed.TotalSeconds);
        MemoryMarshal.AsBytes(_renderer.Buffer.AsSpan()).CopyTo(_frameBytes);
        ViewCanvas.Invalidate();
    }

    private void ViewCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        _frame = CanvasBitmap.CreateFromBytes(sender, _frameBytes, RenderWidth, RenderHeight,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Ignore);
    }

    private void ViewCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_frame is null) return;
        _frame.SetPixelBytes(_frameBytes);

        // Uniform fit, scaled on the GPU with crisp nearest-neighbor sampling.
        double scale = Math.Min(sender.ActualWidth / RenderWidth, sender.ActualHeight / RenderHeight);
        double w = RenderWidth * scale, h = RenderHeight * scale;
        var destination = new Rect((sender.ActualWidth - w) / 2, (sender.ActualHeight - h) / 2, w, h);
        args.DrawingSession.DrawImage(_frame, destination, new Rect(0, 0, RenderWidth, RenderHeight),
            1f, CanvasImageInterpolation.NearestNeighbor);
    }

    private void UpdateStatsPanel()
    {
        TargetsLeftText.Text = _world.AliveCount.ToString();
        MazesClearedText.Text = _mazesCleared.ToString();
        KillsText.Text = _totalKills.ToString();
        ShotsText.Text = _totalShots.ToString();
        AccuracyText.Text = _totalShots == 0 ? "—" : $"{(double)_totalKills / _totalShots:P0}";
        MazeTimeText.Text = (DateTime.Now - _mazeStart).ToString(@"mm\:ss");
        FastestClearText.Text = _fastestClear == TimeSpan.Zero ? "—" : _fastestClear.ToString(@"mm\:ss");
    }

    private void ShowBanner()
    {
        ClearBanner.Visibility = Visibility.Visible;
        Motion.SpringIn(ClearBanner);
    }

    private void HideBanner() => ClearBanner.Visibility = Visibility.Collapsed;

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        _running = !_running;
        StartStopButton.Content = _running ? "⏹  Stop" : "▶  Start";
        StatusText.Text = _running ? "Hunting targets…" : "Paused.";
    }

    private void NewMazeButton_Click(object sender, RoutedEventArgs e)
    {
        NewMaze();
        StatusText.Text = _running ? "Hunting targets…" : "New maze ready.";
    }

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e)
    {
        _mazesCleared = 0;
        _totalKills = 0;
        _totalShots = 0;
        _fastestClear = TimeSpan.Zero;
        UpdateStatsPanel();
        StatusText.Text = "Stats cleared.";
    }
}
