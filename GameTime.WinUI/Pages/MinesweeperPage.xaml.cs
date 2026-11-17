using System.Numerics;
using GameTime.WinUI.Models;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace GameTime.WinUI.Pages;

public sealed partial class MinesweeperPage : Page
{
    private const int Rows = 16, Cols = 16, Mines = 40;
    private const double CellSize = 34, CellPitch = 36;

    public GameStats Stats { get; } = new();

    private MinesweeperGame _game = null!;
    private MinesweeperSolver? _solver;
    private readonly Dictionary<MsCell, Border> _cellViews = new();

    private bool _autoPlaying;
    private bool _gameOver;
    private DateTime _gameStart;

    private static readonly Brush HiddenBrush = new SolidColorBrush(Color.FromArgb(255, 0x4A, 0x4A, 0x5E));
    private static readonly Brush RevealedBrush = new SolidColorBrush(Color.FromArgb(255, 0x2A, 0x2A, 0x36));
    private static readonly Brush MineBrush = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x22, 0x22));
    private static readonly Brush CellBorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x5E, 0x5E, 0x76));
    private static readonly Brush[] NumberBrushes =
    {
        new SolidColorBrush(Colors.Transparent),
        new SolidColorBrush(Color.FromArgb(255, 0x6F, 0xA8, 0xFF)), // 1
        new SolidColorBrush(Color.FromArgb(255, 0x6F, 0xE0, 0x9A)), // 2
        new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x8A, 0x8A)), // 3
        new SolidColorBrush(Color.FromArgb(255, 0xC0, 0x9A, 0xFF)), // 4
        new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xC0, 0x6F)), // 5
        new SolidColorBrush(Color.FromArgb(255, 0x6F, 0xE0, 0xE0)), // 6
        new SolidColorBrush(Colors.White),                          // 7
        new SolidColorBrush(Colors.Gray)                            // 8
    };

    public MinesweeperPage()
    {
        InitializeComponent();
        Motion.KeepCentered(ResultBanner);
        BoardCanvas.Width = Cols * CellPitch;
        BoardCanvas.Height = Rows * CellPitch;
        Loaded += (_, _) => { if (_game is null) StartNewGame(); };
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
        _autoPlaying = false;
    }

    private void StartNewGame()
    {
        _game = new MinesweeperGame(Rows, Cols, Mines);
        _solver = null;
        _gameOver = false;
        _gameStart = DateTime.Now;
        Stats.Status = "Left-click to reveal, right-click to flag.";
        HideBanner();
        BuildBoard();
        UpdateMinesLeft();
    }

    private void BuildBoard()
    {
        BoardCanvas.Children.Clear();
        _cellViews.Clear();

        foreach (var cell in _game.AllCells)
        {
            var view = new Border
            {
                Width = CellSize,
                Height = CellSize,
                CornerRadius = new CornerRadius(4),
                Background = HiddenBrush,
                BorderBrush = CellBorderBrush,
                BorderThickness = new Thickness(1),
                CenterPoint = new Vector3((float)CellSize / 2, (float)CellSize / 2, 0),
                // Cross-fade between hidden/revealed/mine colors on the compositor.
                BackgroundTransition = Motion.Enabled ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(150) } : null,
                Tag = cell,
                Child = new TextBlock
                {
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(view, cell.Col * CellPitch + 1);
            Canvas.SetTop(view, cell.Row * CellPitch + 1);
            view.Tapped += (_, e) => { e.Handled = true; OnCellLeftClick(cell); };
            view.RightTapped += (_, e) => { e.Handled = true; OnCellRightClick(cell); };
            _cellViews[cell] = view;
            BoardCanvas.Children.Add(view);
        }
    }

    private int AnimationMs => (int)(260 - SpeedSlider.Value * 24);

    private void UpdateMinesLeft() =>
        MinesLeftText.Text = Math.Max(_game.MineCount - _game.FlagCount, 0).ToString();

    #region Rendering

    private void RenderCell(MsCell cell, bool animate = false)
    {
        // A new game may have replaced the board while an animation was mid-flight.
        if (!_cellViews.TryGetValue(cell, out var view)) return;
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

        if (animate) Motion.Pop(view);
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
        foreach (var cell in _game.AllCells.Where(c => c.IsMine && c.State != CellState.Flagged).ToList())
        {
            cell.State = CellState.Revealed;
            RenderCell(cell, animate: true);
            await Task.Delay(30);
        }
    }

    private void ShowBanner(string message, Color color)
    {
        ResultBannerText.Text = message;
        ResultBannerText.Foreground = new SolidColorBrush(color);
        ResultBanner.Visibility = Visibility.Visible;
        Motion.SpringIn(ResultBanner);
    }

    private void HideBanner() => ResultBanner.Visibility = Visibility.Collapsed;

    #endregion

    #region Game flow

    private async Task HandleEndOfGameAsync()
    {
        if (_gameOver) return;
        var game = _game;

        if (game.IsLost)
        {
            _gameOver = true;
            Stats.RecordGameEnd(false, 0, DateTime.Now - _gameStart);
            Stats.Status = "💥 Boom! Game over.";
            await ShowAllMinesAsync();
            if (ReferenceEquals(game, _game)) ShowBanner("💥 BOOM!", Colors.OrangeRed);
        }
        else if (game.IsWon)
        {
            _gameOver = true;
            Stats.RecordGameEnd(true, 0, DateTime.Now - _gameStart);
            Stats.Status = "🎉 Field cleared!";
            ShowBanner("🏆 CLEARED!", Colors.Gold);
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
        Stats.Status = "Computer is sweeping…";

        try
        {
            while (_autoPlaying)
            {
                if (_gameOver)
                {
                    await Task.Delay(_game.IsWon ? 1600 : 1200);
                    if (!_autoPlaying) break;
                    StartNewGame();
                    Stats.Status = "Computer is sweeping…";
                    await Task.Delay(300);
                    continue;
                }

                _solver ??= new MinesweeperSolver(_game);
                var action = _solver.ChooseAction();
                if (action is null)
                {
                    await HandleEndOfGameAsync();
                    if (_gameOver) continue;

                    // Nothing left to reveal but the field isn't cleared (e.g. a misplaced manual flag).
                    Stats.Status = "No moves left — remove a wrong flag to continue.";
                    break;
                }

                switch (action.Kind)
                {
                    case MsActionKind.Flag:
                        _game.ToggleFlag(action.Cell);
                        RenderCell(action.Cell, animate: true);
                        UpdateMinesLeft();
                        break;
                    case MsActionKind.Guess:
                        Stats.Status = "🎲 No safe move — guessing…";
                        await Task.Delay(AnimationMs * 2);
                        await RevealWithAnimationAsync(action.Cell);
                        if (!_game.IsLost) Stats.Status = "Computer is sweeping…";
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
            if (Stats.Status.StartsWith("Computer")) Stats.Status = "Autoplay stopped.";
        }
    }

    #endregion

    private void NewGameButton_Click(object sender, RoutedEventArgs e)
    {
        _autoPlaying = false;
        StartNewGame();
    }

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => Stats.Reset();
}
