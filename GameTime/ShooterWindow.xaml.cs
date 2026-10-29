using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using GameTime.Models;

namespace GameTime
{
    public partial class ShooterWindow : Window
    {
        private const int RenderWidth = 800, RenderHeight = 480;

        private MazeWorld _world = null!;
        private ShooterBot _bot = null!;
        private readonly RaycastRenderer _renderer = new(RenderWidth, RenderHeight);
        private WriteableBitmap _bitmap = null!;

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

        public ShooterWindow()
        {
            InitializeComponent();
            _bitmap = new WriteableBitmap(RenderWidth, RenderHeight, 96, 96, PixelFormats.Bgra32, null);
            ViewImage.Source = _bitmap;
            NewMaze();
            RenderFrame();
            CompositionTarget.Rendering += OnRendering;
            Closing += (_, _) => CompositionTarget.Rendering -= OnRendering;
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

        private void OnRendering(object? sender, EventArgs e)
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
            double time = _clock.Elapsed.TotalSeconds;
            _renderer.Render(_world, _bot, time);
            _bitmap.WritePixels(
                new Int32Rect(0, 0, RenderWidth, RenderHeight),
                _renderer.Buffer, RenderWidth * 4, 0);
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
            var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }
            };
            ClearBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            ClearBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private void HideBanner()
        {
            ClearBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ClearBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ClearBanner.Visibility = Visibility.Collapsed;
        }

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
}
