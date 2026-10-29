using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GameTime.Models;

namespace GameTime
{
    public partial class MinesweeperWindow : Window
    {
        private const int Rows = 16, Cols = 16, Mines = 40;

        private MinesweeperGame _game = null!;
        private MinesweeperSolver? _solver;
        private readonly GameStats _stats = new();
        private readonly Dictionary<MsCell, Border> _cellViews = new();

        private bool _autoPlaying;
        private bool _gameOver;
        private DateTime _gameStart;

        private static readonly Brush HiddenBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x5E));
        private static readonly Brush RevealedBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x36));
        private static readonly Brush MineBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x22, 0x22));
        private static readonly Brush[] NumberBrushes =
        {
            Brushes.Transparent,
            new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xFF)), // 1
            new SolidColorBrush(Color.FromRgb(0x6F, 0xE0, 0x9A)), // 2
            new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x8A)), // 3
            new SolidColorBrush(Color.FromRgb(0xC0, 0x9A, 0xFF)), // 4
            new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x6F)), // 5
            new SolidColorBrush(Color.FromRgb(0x6F, 0xE0, 0xE0)), // 6
            Brushes.White,                                        // 7
            Brushes.Gray                                          // 8
        };

        public MinesweeperWindow()
        {
            InitializeComponent();
            DataContext = _stats;
            Loaded += (_, _) => StartNewGame();
            Closing += (_, _) => _autoPlaying = false;
        }

        private void StartNewGame()
        {
            _game = new MinesweeperGame(Rows, Cols, Mines);
            _solver = null;
            _gameOver = false;
            _gameStart = DateTime.Now;
            _stats.Status = "Left-click to reveal, right-click to flag.";
            HideBanner();
            BuildBoard();
            UpdateMinesLeft();
        }

        private void BuildBoard()
        {
            BoardGrid.Children.Clear();
            _cellViews.Clear();
            BoardGrid.Rows = Rows;
            BoardGrid.Columns = Cols;

            foreach (var cell in _game.AllCells)
            {
                var text = new TextBlock
                {
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var border = new Border
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background = HiddenBrush,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x5E, 0x76)),
                    BorderThickness = new Thickness(1),
                    Child = text,
                    Tag = cell,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 1)
                };
                var captured = cell;
                border.MouseLeftButtonDown += (_, e) => { e.Handled = true; OnCellLeftClick(captured); };
                border.MouseRightButtonDown += (_, e) => { e.Handled = true; OnCellRightClick(captured); };
                _cellViews[cell] = border;
                BoardGrid.Children.Add(border);
            }
        }

        private int AnimationMs => (int)(260 - SpeedSlider.Value * 24);

        private void UpdateMinesLeft() =>
            MinesLeftText.Text = Math.Max(_game.MineCount - _game.FlagCount, 0).ToString();

        #region Rendering

        private void RenderCell(MsCell cell, bool animate = false)
        {
            var view = _cellViews[cell];
            var text = (TextBlock)view.Child;

            switch (cell.State)
            {
                case CellState.Hidden:
                    view.Background = HiddenBrush;
                    text.Text = "";
                    break;
                case CellState.Flagged:
                    view.Background = HiddenBrush;
                    text.Text = "🚩";
                    text.FontSize = 16;
                    break;
                case CellState.Revealed:
                    if (cell.IsMine)
                    {
                        view.Background = MineBrush;
                        text.Text = "💣";
                        text.FontSize = 16;
                    }
                    else
                    {
                        view.Background = RevealedBrush;
                        text.FontSize = 20;
                        text.Text = cell.AdjacentMines == 0 ? "" : cell.AdjacentMines.ToString();
                        text.Foreground = NumberBrushes[cell.AdjacentMines];
                    }
                    break;
            }

            if (animate && view.RenderTransform is ScaleTransform scale)
            {
                var pop = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
                    FillBehavior = FillBehavior.Stop
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }

        private async Task RevealWithAnimationAsync(MsCell cell)
        {
            var revealed = _game.Reveal(cell);
            UpdateMinesLeft();
            // Reveal in waves ordered by distance from the click for a ripple effect.
            foreach (var group in revealed
                .OrderBy(c => Math.Abs(c.Row - cell.Row) + Math.Abs(c.Col - cell.Col))
                .GroupBy(c => Math.Abs(c.Row - cell.Row) + Math.Abs(c.Col - cell.Col)))
            {
                foreach (var c in group) RenderCell(c, animate: true);
                if (revealed.Count > 1) await Task.Delay(Math.Max(AnimationMs / 6, 12));
            }
        }

        private async Task ShowAllMinesAsync()
        {
            foreach (var cell in _game.AllCells.Where(c => c.IsMine && c.State != CellState.Flagged))
            {
                cell.State = CellState.Revealed;
                RenderCell(cell, animate: true);
                await Task.Delay(30);
            }
        }

        private void ShowBanner(string message, Brush brush)
        {
            ResultBanner.Text = message;
            ResultBanner.Foreground = brush;
            ResultBanner.Visibility = Visibility.Visible;
            var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }
            };
            ResultBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            ResultBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }

        private void HideBanner()
        {
            ResultBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ResultBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ResultBanner.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Game flow

        private async Task HandleEndOfGameAsync()
        {
            if (_gameOver) return;

            if (_game.IsLost)
            {
                _gameOver = true;
                _stats.RecordGameEnd(false, 0, DateTime.Now - _gameStart);
                _stats.Status = "💥 Boom! Game over.";
                await ShowAllMinesAsync();
                ShowBanner("💥 BOOM!", Brushes.OrangeRed);
            }
            else if (_game.IsWon)
            {
                _gameOver = true;
                _stats.RecordGameEnd(true, 0, DateTime.Now - _gameStart);
                _stats.Status = "🎉 Field cleared!";
                ShowBanner("🏆 CLEARED!", Brushes.Gold);
            }
        }

        private async void OnCellLeftClick(MsCell cell)
        {
            if (_autoPlaying || _gameOver || cell.State == CellState.Flagged) return;
            await RevealWithAnimationAsync(cell);
            await HandleEndOfGameAsync();
        }

        private void OnCellRightClick(MsCell cell)
        {
            if (_autoPlaying || _gameOver) return;
            _game.ToggleFlag(cell);
            RenderCell(cell, animate: true);
            UpdateMinesLeft();
        }

        #endregion

        #region Auto play

        private async void AutoPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPlaying) { _autoPlaying = false; return; }

            _autoPlaying = true;
            AutoPlayButton.Content = "⏹  Stop";
            _stats.Status = "Computer is sweeping…";

            try
            {
                while (_autoPlaying)
                {
                    if (_gameOver)
                    {
                        await Task.Delay(_game.IsWon ? 1600 : 1200);
                        if (!_autoPlaying) break;
                        StartNewGame();
                        _stats.Status = "Computer is sweeping…";
                        await Task.Delay(300);
                        continue;
                    }

                    _solver ??= new MinesweeperSolver(_game);
                    var action = _solver.ChooseAction();
                    if (action is null)
                    {
                        await HandleEndOfGameAsync();
                        continue;
                    }

                    switch (action.Kind)
                    {
                        case MsActionKind.Flag:
                            _game.ToggleFlag(action.Cell);
                            RenderCell(action.Cell, animate: true);
                            UpdateMinesLeft();
                            break;
                        case MsActionKind.Guess:
                            _stats.Status = "🎲 No safe move — guessing…";
                            await Task.Delay(AnimationMs * 2);
                            await RevealWithAnimationAsync(action.Cell);
                            if (!_game.IsLost) _stats.Status = "Computer is sweeping…";
                            break;
                        default:
                            await RevealWithAnimationAsync(action.Cell);
                            break;
                    }

                    await HandleEndOfGameAsync();
                    await Task.Delay(AnimationMs);
                }
            }
            finally
            {
                _autoPlaying = false;
                AutoPlayButton.Content = "▶  Auto Play";
                if (_stats.Status.StartsWith("Computer")) _stats.Status = "Autoplay stopped.";
            }
        }

        #endregion

        private void NewGameButton_Click(object sender, RoutedEventArgs e)
        {
            _autoPlaying = false;
            StartNewGame();
        }

        private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => _stats.Reset();
    }
}
