using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using GameTime.Models;

namespace GameTime
{
    public partial class FreeCellWindow : Window
    {
        private const double CardAspect = 1.452;

        private FreeCellGame _game = null!;
        private FreeCellAutoPlayer? _autoPlayer;
        private readonly GameStats _stats = new();
        private readonly Dictionary<Card, Border> _cardViews = new();
        private readonly Dictionary<Pile, Border> _placeholders = new();
        private readonly Dictionary<string, BitmapImage> _images = new();

        private double _cardWidth = 95, _cardHeight = 138;
        private double _marginX = 20, _marginY = 20, _gapX = 12;
        private double _stackOffset = 26;

        private bool _autoPlaying;
        private bool _animating;
        private DateTime _gameStart;
        private Card? _selectedCard;

        public FreeCellWindow()
        {
            InitializeComponent();
            DataContext = _stats;
            LoadImages();
            Loaded += (_, _) => StartNewGame();
            BoardCanvas.SizeChanged += (_, _) => { ComputeLayout(); LayoutAll(); };
            Closing += (_, _) => _autoPlaying = false;
        }

        #region Setup

        private void LoadImages()
        {
            foreach (Suit s in Enum.GetValues<Suit>())
                foreach (Rank r in Enum.GetValues<Rank>())
                {
                    var c = new Card(s, r);
                    var img = new BitmapImage(new Uri($"pack://application:,,,/Assets/Cards/{c.ImageKey}.png"));
                    img.Freeze();
                    _images[c.ImageKey] = img;
                }
        }

        private void StartNewGame()
        {
            _autoPlaying = false;
            AutoPlayButton.Content = "▶  Auto Play";
            _game = new FreeCellGame();
            _autoPlayer = null;
            _selectedCard = null;
            _stats.CurrentMoves = 0;
            _stats.CardsToFoundation = 0;
            _stats.Status = "Your move!";
            _gameStart = DateTime.Now;
            RebuildBoard();
        }

        private void ComputeLayout()
        {
            double w = Math.Max(BoardCanvas.ActualWidth, 400);
            double h = Math.Max(BoardCanvas.ActualHeight, 300);
            _cardWidth = Math.Min((w - 2 * _marginX - 7 * _gapX) / 8, 120);
            _cardHeight = _cardWidth * CardAspect;
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
            BoardCanvas.Children.Clear();
            _cardViews.Clear();
            _placeholders.Clear();
            ComputeLayout();

            foreach (var pile in _game.AllPiles)
            {
                var ph = new Border
                {
                    Width = _cardWidth,
                    Height = _cardHeight,
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
                    BorderThickness = new Thickness(2),
                    Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
                    Tag = pile
                };
                if (pile.Type == PileType.Foundation)
                    ph.Child = new TextBlock
                    {
                        Text = "A",
                        FontSize = _cardWidth * 0.5,
                        Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                if (pile.Type == PileType.Stock)
                    ph.Child = new TextBlock
                    {
                        Text = "✦",
                        FontSize = _cardWidth * 0.4,
                        Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                ph.MouseLeftButtonDown += (_, e) => { e.Handled = true; OnPileClicked(pile); };
                var pos = PilePosition(pile);
                Canvas.SetLeft(ph, pos.X);
                Canvas.SetTop(ph, pos.Y);
                Canvas.SetZIndex(ph, 0);
                BoardCanvas.Children.Add(ph);
                _placeholders[pile] = ph;
            }

            foreach (var pile in _game.AllPiles)
                foreach (var card in pile.Cards)
                    CreateCardView(card);

            LayoutAll();
        }

        private void CreateCardView(Card card)
        {
            var img = new Image { Stretch = Stretch.Fill, Source = _images[card.ImageKey] };
            var border = new Border
            {
                Width = _cardWidth,
                Height = _cardHeight,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.White,
                Child = img,
                Tag = card,
                Cursor = Cursors.Hand,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = 0.45 }
            };
            border.MouseEnter += (_, _) =>
            {
                if (_autoPlaying || border.RenderTransform is not ScaleTransform scale) return;
                var anim = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(100));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };
            border.MouseLeave += (_, _) =>
            {
                if (border.RenderTransform is not ScaleTransform scale) return;
                var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };
            border.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount >= 2) OnCardDoubleClicked(card);
                else OnCardClicked(card);
            };
            _cardViews[card] = border;
            BoardCanvas.Children.Add(border);
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
            if (_game is null) return;
            foreach (var pile in _game.AllPiles)
            {
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var view = _cardViews[pile.Cards[i]];
                    view.Width = _cardWidth;
                    view.Height = _cardHeight;
                    var pos = CardPosition(pile, i);
                    Canvas.SetZIndex(view, i + 1);
                    Canvas.SetLeft(view, pos.X);
                    Canvas.SetTop(view, pos.Y);
                }
            }
            foreach (var (pile, ph) in _placeholders)
            {
                var pos = PilePosition(pile);
                ph.Width = _cardWidth;
                ph.Height = _cardHeight;
                Canvas.SetLeft(ph, pos.X);
                Canvas.SetTop(ph, pos.Y);
            }
        }

        #endregion

        #region Animation

        private int AnimationMs => (int)(700 - SpeedSlider.Value * 62);

        private static void AnimateTo(Border view, Point target, int ms)
        {
            double fromX = Canvas.GetLeft(view), fromY = Canvas.GetTop(view);
            view.BeginAnimation(Canvas.LeftProperty, null);
            view.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(view, target.X);
            Canvas.SetTop(view, target.Y);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            view.BeginAnimation(Canvas.LeftProperty,
                new DoubleAnimation(fromX, target.X, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            view.BeginAnimation(Canvas.TopProperty,
                new DoubleAnimation(fromY, target.Y, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop });
        }

        private async Task AnimateMoveAsync(FcMove move)
        {
            _animating = true;
            try
            {
                var moved = _game.ApplyMove(move);
                _stats.CurrentMoves = _game.MoveCount;
                _stats.CardsToFoundation = _game.Foundations.Sum(f => f.Count);

                int ms = AnimationMs;
                for (int i = 0; i < moved.Count; i++)
                    Canvas.SetZIndex(_cardViews[moved[i]], 100 + i);

                foreach (var card in moved)
                {
                    var (pile, idx) = Locate(card);
                    AnimateTo(_cardViews[card], CardPosition(pile, idx), ms);
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
            if (_autoPlaying || _animating) return;
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
            if (_autoPlaying || _animating || _selectedCard is null) return;
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
            if (_autoPlaying || _animating) return;
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
            {
                var v = _cardViews[c];
                v.BorderBrush = on ? Brushes.Gold : null;
                v.BorderThickness = new Thickness(on ? 3 : 0);
            }
        }

        private void ClearSelection()
        {
            HighlightSelection(false);
            _selectedCard = null;
        }

        private async Task CheckGameEndAsync()
        {
            if (_game.IsWon)
            {
                _stats.RecordGameEnd(true, _game.MoveCount, DateTime.Now - _gameStart);
                _stats.Status = "🎉 You won! Press New Game.";
                await PlayWinCelebrationAsync();
            }
        }

        #endregion

        #region Win celebration

        private async Task PlayWinCelebrationAsync()
        {
            var rng = new Random();
            double w = BoardCanvas.ActualWidth, h = BoardCanvas.ActualHeight;
            int i = 0;
            foreach (var view in _cardViews.Values)
            {
                Canvas.SetZIndex(view, 200 + i);
                var rotate = new RotateTransform(0);
                var group = new TransformGroup();
                group.Children.Add(view.RenderTransform);
                group.Children.Add(rotate);
                view.RenderTransform = group;

                int ms = 900 + rng.Next(700);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                view.BeginAnimation(Canvas.LeftProperty,
                    new DoubleAnimation(Canvas.GetLeft(view), rng.NextDouble() * w - _cardWidth / 2, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                view.BeginAnimation(Canvas.TopProperty,
                    new DoubleAnimation(Canvas.GetTop(view), h - _cardHeight * (0.3 + rng.NextDouble() * 0.9), TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                rotate.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, rng.Next(-360, 360), TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
                i++;
                if (i % 8 == 0) await Task.Delay(40);
            }

            WinBanner.Visibility = Visibility.Visible;
            var pop = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 5 }
            };
            WinBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            WinBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            await Task.Delay(1400);
        }

        private void HideWinBanner()
        {
            WinBannerScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            WinBannerScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            WinBanner.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Auto play

        private async void AutoPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPlaying) { _autoPlaying = false; return; }

            _autoPlaying = true;
            AutoPlayButton.Content = "⏹  Stop";
            _stats.Status = "Computer is playing…";
            ClearSelection();

            try
            {
                while (_autoPlaying)
                {
                    _autoPlayer ??= new FreeCellAutoPlayer(_game);
                    var move = _autoPlayer.ChooseMove();

                    if (move is null || _game.IsWon)
                    {
                        bool won = _game.IsWon;
                        _stats.RecordGameEnd(won, _game.MoveCount, DateTime.Now - _gameStart);
                        _stats.Status = won ? "🏆 Computer won! Dealing next game…" : "Stuck — dealing next game…";
                        if (won) await PlayWinCelebrationAsync();
                        await Task.Delay(won ? 800 : 900);
                        if (!_autoPlaying) break;

                        _game = new FreeCellGame();
                        _autoPlayer = new FreeCellAutoPlayer(_game);
                        _stats.CurrentMoves = 0;
                        _stats.CardsToFoundation = 0;
                        _gameStart = DateTime.Now;
                        RebuildBoard();
                        _stats.Status = "Computer is playing…";
                        await Task.Delay(400);
                        continue;
                    }

                    await AnimateMoveAsync(move);
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

        #region Buttons

        private void NewGameButton_Click(object sender, RoutedEventArgs e) => StartNewGame();

        private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => _stats.Reset();

        private async void HintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPlaying || _animating) return;
            var player = new FreeCellAutoPlayer(_game);
            var move = player.ChooseMove();
            if (move is null) { _stats.Status = "No moves available."; return; }
            _stats.Status = "Hint played.";
            await AnimateMoveAsync(move);
            await CheckGameEndAsync();
        }

        #endregion
    }
}
