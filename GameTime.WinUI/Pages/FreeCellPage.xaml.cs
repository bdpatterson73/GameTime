using System.Numerics;
using GameTime.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace GameTime.WinUI.Pages;

public sealed partial class FreeCellPage : Page
{
    private const double CardAspect = 1.452;
    private const int DealMs = 260;

    public GameStats Stats { get; } = new();

    private FreeCellGame _game = null!;
    private FreeCellAutoPlayer? _autoPlayer;
    private readonly Dictionary<Card, Border> _cardViews = new();
    private readonly Dictionary<Pile, Border> _placeholders = new();
    private readonly DoubleClickTracker _clicks = new();

    private double _cardWidth = 95, _cardHeight = 138;
    private readonly double _marginX = 20, _marginY = 20, _gapX = 12;
    private double _stackOffset = 26;

    private bool _autoPlaying;
    private bool _animating;
    private bool _dealing;
    private bool _celebrating;
    private int _dealId;
    private Task _deal = Task.CompletedTask;
    private DateTime _gameStart;
    private Card? _selectedCard;

    private bool Busy => _animating || _dealing;

    public FreeCellPage()
    {
        InitializeComponent();
        Motion.KeepCentered(WinBanner);
        Loaded += (_, _) => { if (_game is null) StartNewGame(); };
        BoardCanvas.SizeChanged += (_, e) =>
        {
            BoardCanvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            ComputeLayout();
            LayoutAll();
        };
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
        // Pointer-exit never arrives once this cached page leaves the tree.
        foreach (var view in _cardViews.Values)
            CardTable.SetHover(view, false, changeElevation: !Busy);
    }

    #region Setup

    private void StartNewGame()
    {
        _autoPlaying = false;
        AutoPlayButton.Content = "▶  Auto Play";
        _game = new FreeCellGame();
        _autoPlayer = null;
        _selectedCard = null;
        Stats.CurrentMoves = 0;
        Stats.CardsToFoundation = 0;
        Stats.Status = "Your move!";
        _gameStart = DateTime.Now;
        RebuildBoard();
    }

    private void ComputeLayout()
    {
        double w = Math.Max(BoardCanvas.ActualWidth, 400);
        double h = Math.Max(BoardCanvas.ActualHeight, 300);
        _cardWidth = Math.Floor(Math.Min((w - 2 * _marginX - 7 * _gapX) / 8, 120));
        _cardHeight = Math.Round(_cardWidth * CardAspect);
        _stackOffset = Math.Min(_cardHeight * 0.24, (h - _marginY * 3 - _cardHeight * 2) / 19);
    }

    private Point PilePosition(Pile pile)
    {
        double colW = _cardWidth + _gapX;
        return pile.Type switch
        {
            // Free cells: top-left four slots
            PileType.Stock => new Point(_marginX + pile.Index * colW, _marginY),
            // Foundations: top-right four slots
            PileType.Foundation => new Point(_marginX + (4 + pile.Index) * colW, _marginY),
            // Cascades
            _ => new Point(_marginX + pile.Index * colW, _marginY * 2 + _cardHeight)
        };
    }

    private Point CardPosition(Pile pile, int indexInPile)
    {
        var p = PilePosition(pile);
        if (pile.Type != PileType.Tableau) return p;
        return new Point(p.X, p.Y + indexInPile * _stackOffset);
    }

    private void RebuildBoard()
    {
        HideWinBanner();
        _celebrating = false;
        BoardCanvas.Children.Clear();
        _cardViews.Clear();
        _placeholders.Clear();
        ComputeLayout();

        foreach (var pile in _game.AllPiles)
        {
            var ph = pile.Type switch
            {
                PileType.Foundation => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, "A", 0.5, 70),
                PileType.Stock => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, "✦", 0.4, 70),
                _ => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, null, 0, 0)
            };
            ph.PointerPressed += (_, e) =>
            {
                if (!CardTable.IsPrimaryPress(ph, e)) return;
                e.Handled = true;
                OnPileClicked(pile);
            };
            Canvas.SetZIndex(ph, 0);
            BoardCanvas.Children.Add(ph);
            _placeholders[pile] = ph;
        }

        foreach (var pile in _game.AllPiles)
            foreach (var card in pile.Cards)
                CreateCardView(card);

        _deal = DealAsync();
    }

    /// <summary>
    /// Deals the deck out across the cascades one card at a time, as it was shuffled.
    /// Each card's flight runs on the compositor; this only staggers the launches.
    /// </summary>
    private async Task DealAsync()
    {
        int id = ++_dealId;
        if (!Motion.Enabled || !IsLoaded)
        {
            _dealing = false;
            LayoutAll();
            return;
        }

        _dealing = true;
        try
        {
            var origin = PilePosition(_game.Cells[0]);
            foreach (var (pile, ph) in _placeholders)
                CardTable.Place(ph, PilePosition(pile));
            foreach (var view in _cardViews.Values)
                CardTable.Place(view, origin);
            await Task.Delay(30);

            int columns = _game.Cascades.Length;
            int total = _game.Cascades.Sum(c => c.Count);
            for (int i = 0; i < total; i++)
            {
                if (id != _dealId) return;
                var pile = _game.Cascades[i % columns];
                int row = i / columns;
                var view = _cardViews[pile.Cards[row]];
                Canvas.SetZIndex(view, 100 + i);
                CardTable.Place(view, CardPosition(pile, row), DealMs, lifted: true);
                if (i % 4 == 3) await Task.Delay(16);
            }
            await Task.Delay(DealMs);
        }
        finally
        {
            if (id == _dealId) _dealing = false;
        }

        if (id == _dealId) LayoutAll();
    }

    private void CreateCardView(Card card)
    {
        var view = CardTable.CreateCardView(card, _cardWidth, _cardHeight);
        ((Image)view.Child).Source = CardTable.FaceImage(card);
        view.PointerEntered += (_, _) =>
        {
            if (!_autoPlaying && !Busy) CardTable.SetHover(view, true);
        };
        // While cards are moving, leave elevation to the move itself; it settles on landing.
        view.PointerExited += (_, _) => CardTable.SetHover(view, false, changeElevation: !Busy);
        view.PointerPressed += (_, e) =>
        {
            if (!CardTable.IsPrimaryPress(view, e)) return;
            e.Handled = true;
            if (_clicks.IsDoubleClick(card)) OnCardDoubleClicked(card);
            else OnCardClicked(card);
        };
        _cardViews[card] = view;
        BoardCanvas.Children.Add(view);
    }

    private (Pile pile, int index) Locate(Card card)
    {
        foreach (var pile in _game.AllPiles)
        {
            int i = pile.Cards.IndexOf(card);
            if (i >= 0) return (pile, i);
        }
        throw new InvalidOperationException("Card not found");
    }

    private void LayoutAll()
    {
        // Scattered celebration cards stay where they landed until the next deal.
        if (_game is null || _celebrating) return;
        foreach (var pile in _game.AllPiles)
        {
            for (int i = 0; i < pile.Cards.Count; i++)
            {
                var view = _cardViews[pile.Cards[i]];
                CardTable.Resize(view, _cardWidth, _cardHeight);
                Canvas.SetZIndex(view, i + 1);
                CardTable.Place(view, CardPosition(pile, i));
            }
        }
        foreach (var (pile, ph) in _placeholders)
        {
            CardTable.Resize(ph, _cardWidth, _cardHeight);
            CardTable.Place(ph, PilePosition(pile));
        }
    }

    #endregion

    #region Animation

    private int AnimationMs => (int)(700 - SpeedSlider.Value * 62);

    private async Task AnimateMoveAsync(FcMove move)
    {
        _animating = true;
        try
        {
            var moved = _game.ApplyMove(move);
            Stats.CurrentMoves = _game.MoveCount;
            Stats.CardsToFoundation = _game.Foundations.Sum(f => f.Count);

            int ms = AnimationMs;
            for (int i = 0; i < moved.Count; i++)
            {
                var view = _cardViews[moved[i]];
                Canvas.SetZIndex(view, 100 + i);
                var (pile, idx) = Locate(moved[i]);
                view.Scale = Vector3.One;
                CardTable.Place(view, CardPosition(pile, idx), ms, lifted: true);
            }
            await Task.Delay(ms + 20);
            LayoutAll();
        }
        finally
        {
            _animating = false;
        }
    }

    #endregion

    #region Manual play

    private async void OnCardClicked(Card card)
    {
        if (_autoPlaying || Busy) return;
        var (pile, _) = Locate(card);

        if (_selectedCard is null)
        {
            _selectedCard = card;
            HighlightSelection(true);
            return;
        }
        if (_selectedCard == card) { ClearSelection(); return; }

        var move = BuildMove(_selectedCard, pile);
        ClearSelection();
        if (move is not null)
        {
            await AnimateMoveAsync(move);
            await CheckGameEndAsync();
        }
    }

    private async void OnPileClicked(Pile pile)
    {
        if (_autoPlaying || Busy || _selectedCard is null) return;
        var move = BuildMove(_selectedCard, pile);
        ClearSelection();
        if (move is not null)
        {
            await AnimateMoveAsync(move);
            await CheckGameEndAsync();
        }
    }

    private FcMove? BuildMove(Card selected, Pile target)
    {
        var (fromPile, index) = Locate(selected);
        if (fromPile == target) return null;
        int count = fromPile.Count - index;

        if (target.Type == PileType.Foundation)
        {
            if (count != 1 || !FreeCellGame.CanPlaceOnFoundation(selected, target)) return null;
            var kind = fromPile.Type == PileType.Stock ? FcMoveKind.CellToFoundation : FcMoveKind.CascadeToFoundation;
            return new FcMove(kind, fromPile, target);
        }
        if (target.Type == PileType.Stock) // free cell
        {
            if (count != 1 || target.Count > 0 || fromPile.Type != PileType.Tableau) return null;
            return new FcMove(FcMoveKind.CascadeToCell, fromPile, target);
        }
        if (target.Type == PileType.Tableau)
        {
            if (fromPile.Type == PileType.Stock)
                return FreeCellGame.CanPlaceOnCascade(selected, target)
                    ? new FcMove(FcMoveKind.CellToCascade, fromPile, target)
                    : null;

            // Cascade to cascade: the selected card must head a valid run and fit the limit.
            if (count > FreeCellGame.RunLength(fromPile)) return null;
            int limit = target.Count == 0
                ? (_game.FreeCellCount + 1) * _game.EmptyCascadeCount
                : _game.MaxSupermove;
            if (count > Math.Max(limit, 1)) return null;
            if (!FreeCellGame.CanPlaceOnCascade(selected, target)) return null;
            return new FcMove(FcMoveKind.CascadeToCascade, fromPile, target, count);
        }
        return null;
    }

    private async void OnCardDoubleClicked(Card card)
    {
        if (_autoPlaying || Busy) return;
        ClearSelection();
        var (pile, index) = Locate(card);
        if (index != pile.Count - 1) return;
        foreach (var f in _game.Foundations)
        {
            if (FreeCellGame.CanPlaceOnFoundation(card, f))
            {
                var kind = pile.Type == PileType.Stock ? FcMoveKind.CellToFoundation : FcMoveKind.CascadeToFoundation;
                await AnimateMoveAsync(new FcMove(kind, pile, f));
                await CheckGameEndAsync();
                return;
            }
        }
    }

    private void HighlightSelection(bool on)
    {
        if (_selectedCard is null) return;
        var (pile, index) = Locate(_selectedCard);
        var cards = pile.Type == PileType.Tableau
            ? pile.Cards.Skip(index)
            : new[] { _selectedCard }.AsEnumerable();
        foreach (var c in cards)
            CardTable.SetSelected(_cardViews[c], on);
    }

    private void ClearSelection()
    {
        HighlightSelection(false);
        _selectedCard = null;
    }

    private async Task CheckGameEndAsync()
    {
        if (!_game.IsWon || _celebrating) return;
        Stats.RecordGameEnd(true, _game.MoveCount, DateTime.Now - _gameStart);
        Stats.Status = "🎉 You won! Press New Game.";
        await PlayWinCelebrationAsync();
    }

    #endregion

    #region Win celebration

    private async Task PlayWinCelebrationAsync()
    {
        var game = _game;
        _celebrating = true;
        var rng = new Random();
        double w = BoardCanvas.ActualWidth, h = BoardCanvas.ActualHeight;
        int i = 0;
        foreach (var view in _cardViews.Values.ToList())
        {
            Canvas.SetZIndex(view, 200 + i);
            var target = new Point(
                rng.NextDouble() * w - _cardWidth / 2,
                h - _cardHeight * (0.3 + rng.NextDouble() * 0.9));
            CardTable.Scatter(view, target, rng.Next(-360, 360), 900 + rng.Next(700));
            i++;
            if (i % 8 == 0) await Task.Delay(40);
            if (!ReferenceEquals(game, _game)) return; // a new deal replaced the board
        }

        WinBanner.Visibility = Visibility.Visible;
        Motion.SpringIn(WinBanner);
        await Task.Delay(1400);
    }

    private void HideWinBanner() => WinBanner.Visibility = Visibility.Collapsed;

    #endregion

    #region Auto play

    private async void AutoPlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPlaying) { _autoPlaying = false; return; }

        _autoPlaying = true;
        AutoPlayButton.Content = "⏹  Stop";
        Stats.Status = "Computer is playing…";
        ClearSelection();

        try
        {
            await _deal;
            while (_autoPlaying)
            {
                _autoPlayer ??= new FreeCellAutoPlayer(_game);
                var move = _autoPlayer.ChooseMove();

                if (move is null || _game.IsWon)
                {
                    bool won = _game.IsWon;
                    if (!_celebrating)
                    {
                        Stats.RecordGameEnd(won, _game.MoveCount, DateTime.Now - _gameStart);
                        Stats.Status = won ? "🏆 Computer won! Dealing next game…" : "Stuck — dealing next game…";
                        if (won) await PlayWinCelebrationAsync();
                    }
                    await Task.Delay(won ? 800 : 900);
                    if (!_autoPlaying) break;

                    _game = new FreeCellGame();
                    _autoPlayer = new FreeCellAutoPlayer(_game);
                    Stats.CurrentMoves = 0;
                    Stats.CardsToFoundation = 0;
                    _gameStart = DateTime.Now;
                    RebuildBoard();
                    Stats.Status = "Computer is playing…";
                    await _deal;
                    await Task.Delay(200);
                    continue;
                }

                await AnimateMoveAsync(move);
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

    #region Buttons

    private void NewGameButton_Click(object sender, RoutedEventArgs e) => StartNewGame();

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => Stats.Reset();

    private async void HintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPlaying || Busy) return;
        var player = new FreeCellAutoPlayer(_game);
        var move = player.ChooseMove();
        if (move is null) { Stats.Status = "No moves available."; return; }
        Stats.Status = "Hint played.";
        await AnimateMoveAsync(move);
        await CheckGameEndAsync();
    }

    #endregion
}
