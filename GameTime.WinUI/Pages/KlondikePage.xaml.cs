using System.Numerics;
using GameTime.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;

namespace GameTime.WinUI.Pages;

public sealed partial class KlondikePage : Page
{
    private const double CardAspect = 1.452; // height / width
    private const int DealMs = 260;

    public GameStats Stats { get; } = new();

    private SolitaireGame _game = null!;
    private AutoPlayer? _autoPlayer;
    private readonly Dictionary<Card, Border> _cardViews = new();
    private readonly Dictionary<Pile, Border> _placeholders = new();
    private readonly Dictionary<Card, bool> _shownFaceUp = new();
    private readonly HashSet<Card> _flipping = new();
    private readonly DoubleClickTracker _clicks = new();

    private double _cardWidth = 100, _cardHeight = 145;
    private readonly double _marginX = 20, _marginY = 20, _gapX = 16;
    private double _faceUpOffset = 28, _faceDownOffset = 10;

    private bool _autoPlaying;
    private bool _animating;
    private bool _dealing;
    private bool _celebrating;
    private int _dealId;
    private Task _deal = Task.CompletedTask;
    private DateTime _gameStart;
    private Card? _selectedCard;

    private bool Busy => _animating || _dealing;

    public KlondikePage()
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
        StopAutoPlay();
        // Pointer-exit never arrives once this cached page leaves the tree.
        foreach (var (card, view) in _cardViews)
            if (!_flipping.Contains(card)) CardTable.SetHover(view, false, changeElevation: !Busy);
    }

    #region Setup

    private int DrawMode => Draw3Check?.IsChecked == true ? 3 : 1;

    private void StartNewGame()
    {
        StopAutoPlay();
        _game = new SolitaireGame(drawCount: DrawMode);
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
        _cardWidth = Math.Floor(Math.Min((w - 2 * _marginX - 6 * _gapX) / 7, 130));
        _cardHeight = Math.Round(_cardWidth * CardAspect);
        _faceUpOffset = Math.Min(_cardHeight * 0.26, (h - _marginY * 3 - _cardHeight * 2) / 14);
        _faceDownOffset = _faceUpOffset * 0.4;
    }

    private Point PilePosition(Pile pile) => pile.Type switch
    {
        PileType.Stock => new Point(_marginX, _marginY),
        PileType.Waste => new Point(_marginX + _cardWidth + _gapX, _marginY),
        PileType.Foundation => new Point(_marginX + (3 + pile.Index) * (_cardWidth + _gapX), _marginY),
        _ => new Point(_marginX + pile.Index * (_cardWidth + _gapX), _marginY * 2 + _cardHeight)
    };

    private Point CardPosition(Pile pile, int indexInPile)
    {
        var p = PilePosition(pile);
        if (pile.Type == PileType.Waste)
        {
            // Fan the most recently drawn cards so a draw-3 is visible.
            int fanStart = Math.Max(0, pile.Count - _game.DrawCount);
            int fanIndex = Math.Max(0, indexInPile - fanStart);
            return new Point(p.X + fanIndex * _cardWidth * 0.3, p.Y);
        }
        if (pile.Type != PileType.Tableau) return p;
        double y = p.Y;
        for (int i = 0; i < indexInPile; i++)
            y += pile.Cards[i].IsFaceUp ? _faceUpOffset : _faceDownOffset;
        return new Point(p.X, y);
    }

    private void RebuildBoard()
    {
        HideWinBanner();
        _celebrating = false;
        BoardCanvas.Children.Clear();
        _cardViews.Clear();
        _shownFaceUp.Clear();
        _flipping.Clear();
        _placeholders.Clear();
        ComputeLayout();

        foreach (var pile in _game.AllPiles)
        {
            var ph = pile.Type switch
            {
                PileType.Foundation => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, "A", 0.5, 70),
                PileType.Stock => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, "↻", 0.5, 90),
                _ => CardTable.CreatePlaceholder(pile, _cardWidth, _cardHeight, null, 0, 0)
            };
            ph.PointerPressed += (_, e) =>
            {
                if (!CardTable.IsPrimaryPress(ph, e)) return;
                e.Handled = true;
                if (pile.Type == PileType.Stock) OnStockClicked();
                else OnPileClicked(pile);
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
    /// Deals the tableau out from the stock, row by row, then turns the top cards over.
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
            var origin = PilePosition(_game.Stock);
            foreach (var (pile, ph) in _placeholders)
                CardTable.Place(ph, PilePosition(pile));
            foreach (var pile in _game.AllPiles)
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var view = _cardViews[pile.Cards[i]];
                    Canvas.SetZIndex(view, i + 1);
                    CardTable.Place(view, origin);
                }
            await Task.Delay(30);

            int dealt = 0;
            for (int row = 0; row < _game.Tableaus.Length; row++)
            {
                for (int col = row; col < _game.Tableaus.Length; col++)
                {
                    if (id != _dealId) return;
                    var pile = _game.Tableaus[col];
                    var view = _cardViews[pile.Cards[row]];
                    Canvas.SetZIndex(view, 100 + dealt);
                    CardTable.Place(view, CardPosition(pile, row), DealMs, lifted: true);
                    if (++dealt % 2 == 0) await Task.Delay(16);
                }
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
        view.PointerEntered += (_, _) =>
        {
            if (_autoPlaying || Busy || !card.IsFaceUp || _flipping.Contains(card)) return;
            CardTable.SetHover(view, true);
        };
        view.PointerExited += (_, _) =>
        {
            // While cards are moving, leave elevation to the move itself; it settles on landing.
            if (!_flipping.Contains(card)) CardTable.SetHover(view, false, changeElevation: !Busy);
        };
        view.PointerPressed += (_, e) =>
        {
            if (!CardTable.IsPrimaryPress(view, e)) return;
            e.Handled = true;
            if (_clicks.IsDoubleClick(card)) OnCardDoubleClicked(card);
            else OnCardClicked(card);
        };
        _cardViews[card] = view;
        BoardCanvas.Children.Add(view);

        // Every card starts face down; the layout pass after the deal turns the top cards over.
        ((Image)view.Child).Source = CardTable.BackImage;
        _shownFaceUp[card] = false;
    }

    private void UpdateCardFace(Card card)
    {
        var view = _cardViews[card];
        var img = (Image)view.Child;
        bool wasUp = _shownFaceUp.TryGetValue(card, out var up) && up;
        _shownFaceUp[card] = card.IsFaceUp;

        if (wasUp == card.IsFaceUp || !IsLoaded || !Motion.Enabled)
        {
            if (!_flipping.Contains(card))
                img.Source = card.IsFaceUp ? CardTable.FaceImage(card) : CardTable.BackImage;
            return;
        }

        _ = FlipAsync(card, view, img);
    }

    /// <summary>3-D style flip: squeeze, swap face, expand.</summary>
    private async Task FlipAsync(Card card, Border view, Image img)
    {
        int half = Math.Max(AnimationMs / 3, 70);
        _flipping.Add(card);
        img.Source ??= CardTable.BackImage;
        view.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(half) };
        view.Scale = new Vector3(0, 1, 1);
        await Task.Delay(half);

        img.Source = card.IsFaceUp ? CardTable.FaceImage(card) : CardTable.BackImage;
        view.Scale = Vector3.One;
        await Task.Delay(half);

        view.ScaleTransition = CardTable.HoverTransition();
        _flipping.Remove(card);
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
                var card = pile.Cards[i];
                var view = _cardViews[card];
                CardTable.Resize(view, _cardWidth, _cardHeight);
                UpdateCardFace(card);
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

    private int AnimationMs => (int)(700 - SpeedSlider.Value * 62); // 1=slow, 10=fast

    private async Task AnimateMoveAsync(Move move)
    {
        _animating = true;
        try
        {
            var moved = _game.ApplyMove(move);
            Stats.CurrentMoves = _game.MoveCount;
            Stats.CardsToFoundation = _game.Foundations.Sum(f => f.Count);

            int ms = AnimationMs;
            if (move.Kind == MoveKind.RecycleWaste)
            {
                LayoutAll();
                await Task.Delay(Math.Max(ms / 2, 60));
                return;
            }

            // Raise moved cards above everything and fly them into place; LayoutAll sets them down.
            for (int i = 0; i < moved.Count; i++)
            {
                var card = moved[i];
                var view = _cardViews[card];
                Canvas.SetZIndex(view, 100 + i);
                var (pile, idx) = Locate(card);
                UpdateCardFace(card);
                if (!_flipping.Contains(card)) view.Scale = Vector3.One;
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

    private async void OnStockClicked()
    {
        if (_autoPlaying || Busy) return;
        ClearSelection();
        var moves = _game.GetLegalMoves();
        var m = moves.FirstOrDefault(m => m.Kind is MoveKind.Draw or MoveKind.RecycleWaste);
        if (m is null) return;
        await AnimateMoveAsync(m);
    }

    private async void OnCardClicked(Card card)
    {
        if (_autoPlaying || Busy) return;
        var (pile, _) = Locate(card);

        if (pile.Type == PileType.Stock) { OnStockClicked(); return; }
        if (!card.IsFaceUp) return;

        if (_selectedCard is null)
        {
            // Select this card (and its run if tableau).
            _selectedCard = card;
            HighlightSelection(true);
            return;
        }

        if (_selectedCard == card) { ClearSelection(); return; }

        // Try moving the selected run onto this card's pile.
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

    private Move? BuildMove(Card selected, Pile target)
    {
        var (fromPile, index) = Locate(selected);
        int count = fromPile.Count - index;

        if (target.Type == PileType.Foundation)
        {
            if (count != 1 || !SolitaireGame.CanPlaceOnFoundation(selected, target)) return null;
            var kind = fromPile.Type == PileType.Waste ? MoveKind.WasteToFoundation : MoveKind.TableauToFoundation;
            return new Move(kind, fromPile, target);
        }
        if (target.Type == PileType.Tableau)
        {
            if (!SolitaireGame.CanPlaceOnTableau(selected, target)) return null;
            if (fromPile.Type == PileType.Waste)
                return count == 1 ? new Move(MoveKind.WasteToTableau, fromPile, target) : null;
            if (fromPile.Type == PileType.Tableau && fromPile != target)
                return new Move(MoveKind.TableauToTableau, fromPile, target, count);
        }
        return null;
    }

    private async void OnCardDoubleClicked(Card card)
    {
        if (_autoPlaying || Busy || !card.IsFaceUp) return;
        ClearSelection();
        var (pile, index) = Locate(card);
        if (index != pile.Count - 1) return;
        foreach (var f in _game.Foundations)
        {
            if (SolitaireGame.CanPlaceOnFoundation(card, f))
            {
                var kind = pile.Type == PileType.Waste ? MoveKind.WasteToFoundation : MoveKind.TableauToFoundation;
                await AnimateMoveAsync(new Move(kind, pile, f));
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

    private async Task PlayWinCelebrationAsync()
    {
        // Burst every card outward with spin, then show the banner.
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
        if (_autoPlaying) { StopAutoPlay(); return; }

        _autoPlaying = true;
        AutoPlayButton.Content = "⏹  Stop";
        Stats.Status = "Computer is playing…";
        ClearSelection();

        try
        {
            await _deal;
            while (_autoPlaying)
            {
                _autoPlayer ??= new AutoPlayer(_game);
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

                    _game = new SolitaireGame(drawCount: DrawMode);
                    _autoPlayer = new AutoPlayer(_game);
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

    private void StopAutoPlay() => _autoPlaying = false;

    #endregion

    #region Buttons

    private void NewGameButton_Click(object sender, RoutedEventArgs e) => StartNewGame();

    private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => Stats.Reset();

    private void DrawModeChanged(object sender, RoutedEventArgs e)
    {
        if (_game is null || _autoPlaying) return;
        StartNewGame();
    }

    private async void HintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPlaying || Busy) return;
        var player = new AutoPlayer(_game);
        var move = player.ChooseMove();
        if (move is null) { Stats.Status = "No moves available."; return; }
        Stats.Status = $"Hint: {Describe(move)}";
        await AnimateMoveAsync(move);
        await CheckGameEndAsync();
    }

    private static string Describe(Move m) => m.Kind switch
    {
        MoveKind.Draw => "draw from stock",
        MoveKind.RecycleWaste => "recycle the waste pile",
        MoveKind.WasteToFoundation or MoveKind.TableauToFoundation => "move to foundation",
        MoveKind.WasteToTableau => "play from waste",
        _ => "move between columns"
    };

    #endregion
}
