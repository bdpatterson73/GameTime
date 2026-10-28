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
    public partial class MainWindow : Window
    {
        private const double CardAspect = 1.452; // height / width

        private SolitaireGame _game = null!;
        private AutoPlayer? _autoPlayer;
        private readonly GameStats _stats = new();
        private readonly Dictionary<Card, Border> _cardViews = new();
        private readonly Dictionary<Pile, Border> _placeholders = new();
        private readonly Dictionary<string, BitmapImage> _images = new();
        private BitmapImage _backImage = null!;

        private double _cardWidth = 100, _cardHeight = 145;
        private double _marginX = 20, _marginY = 20, _gapX = 16;
        private double _faceUpOffset = 28, _faceDownOffset = 10;

        private bool _autoPlaying;
        private bool _animating;
        private DateTime _gameStart;
        private Card? _selectedCard;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _stats;
            LoadImages();
            Loaded += (_, _) => StartNewGame();
            BoardCanvas.SizeChanged += (_, _) => { ComputeLayout(); LayoutAll(false); };
        }

        #region Setup

        private void LoadImages()
        {
            _backImage = LoadImage("back");
            foreach (Suit s in Enum.GetValues<Suit>())
                foreach (Rank r in Enum.GetValues<Rank>())
                {
                    var c = new Card(s, r);
                    _images[c.ImageKey] = LoadImage(c.ImageKey);
                }
        }

        private static BitmapImage LoadImage(string key)
        {
            var img = new BitmapImage(new Uri($"pack://application:,,,/Assets/Cards/{key}.png"));
            img.Freeze();
            return img;
        }

        private int DrawMode => Draw3Check?.IsChecked == true ? 3 : 1;

        private void StartNewGame()
        {
            StopAutoPlay();
            _game = new SolitaireGame(drawCount: DrawMode);
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
            _cardWidth = Math.Min((w - 2 * _marginX - 6 * _gapX) / 7, 130);
            _cardHeight = _cardWidth * CardAspect;
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
            BoardCanvas.Children.Clear();
            _cardViews.Clear();
            _shownFaceUp.Clear();
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
                {
                    ph.Child = new TextBlock
                    {
                        Text = "↻",
                        FontSize = _cardWidth * 0.5,
                        Foreground = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ph.MouseLeftButtonDown += (_, e) => { e.Handled = true; OnStockClicked(); };
                    ph.Cursor = Cursors.Hand;
                }
                else
                {
                    ph.MouseLeftButtonDown += (_, e) => { e.Handled = true; OnPileClicked(pile); };
                }
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

            LayoutAll(false);
        }

        private void CreateCardView(Card card)
        {
            var img = new Image { Stretch = Stretch.Fill };
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
                if (_autoPlaying || !card.IsFaceUp || border.RenderTransform is not ScaleTransform scale) return;
                var anim = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(100));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                ((DropShadowEffect)border.Effect).Opacity = 0.75;
            };
            border.MouseLeave += (_, _) =>
            {
                if (border.RenderTransform is not ScaleTransform scale) return;
                var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
                ((DropShadowEffect)border.Effect).Opacity = 0.45;
            };
            border.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount >= 2) OnCardDoubleClicked(card);
                else OnCardClicked(card);
            };
            _cardViews[card] = border;
            BoardCanvas.Children.Add(border);
            UpdateCardFace(card);
        }

        private readonly Dictionary<Card, bool> _shownFaceUp = new();

        private void UpdateCardFace(Card card)
        {
            var view = _cardViews[card];
            var img = (Image)view.Child;
            bool wasUp = _shownFaceUp.TryGetValue(card, out var up) && up;

            if (wasUp == card.IsFaceUp || !IsLoaded)
            {
                img.Source = card.IsFaceUp ? _images[card.ImageKey] : _backImage;
                _shownFaceUp[card] = card.IsFaceUp;
                return;
            }

            // 3-D style flip: squeeze, swap face, expand.
            _shownFaceUp[card] = card.IsFaceUp;
            if (view.RenderTransform is not ScaleTransform scale)
            {
                img.Source = card.IsFaceUp ? _images[card.ImageKey] : _backImage;
                return;
            }
            int half = Math.Max(AnimationMs / 3, 70);
            var shrink = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(half)) { FillBehavior = FillBehavior.Stop };
            shrink.Completed += (_, _) =>
            {
                img.Source = card.IsFaceUp ? _images[card.ImageKey] : _backImage;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(half)) { FillBehavior = FillBehavior.Stop });
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
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

        private void LayoutAll(bool animate)
        {
            if (_game == null) return;
            foreach (var pile in _game.AllPiles)
            {
                for (int i = 0; i < pile.Cards.Count; i++)
                {
                    var card = pile.Cards[i];
                    var view = _cardViews[card];
                    view.Width = _cardWidth;
                    view.Height = _cardHeight;
                    UpdateCardFace(card);
                    var pos = CardPosition(pile, i);
                    Canvas.SetZIndex(view, i + 1);
                    if (animate) AnimateTo(view, pos, 180);
                    else { Canvas.SetLeft(view, pos.X); Canvas.SetTop(view, pos.Y); }
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

        private int AnimationMs => (int)(700 - SpeedSlider.Value * 62); // 1=slow, 10=fast

        private static void AnimateTo(Border view, Point target, int ms)
        {
            double fromX = Canvas.GetLeft(view), fromY = Canvas.GetTop(view);
            view.BeginAnimation(Canvas.LeftProperty, null);
            view.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(view, target.X);
            Canvas.SetTop(view, target.Y);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var ax = new DoubleAnimation(fromX, target.X, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            var ay = new DoubleAnimation(fromY, target.Y, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
            view.BeginAnimation(Canvas.LeftProperty, ax);
            view.BeginAnimation(Canvas.TopProperty, ay);
        }

        private async Task AnimateMoveAsync(Move move)
        {
            _animating = true;
            try
            {
                var moved = _game.ApplyMove(move);
                _stats.CurrentMoves = _game.MoveCount;
                _stats.CardsToFoundation = _game.Foundations.Sum(f => f.Count);

                int ms = AnimationMs;
                if (move.Kind == MoveKind.RecycleWaste)
                {
                    LayoutAll(false);
                    await Task.Delay(Math.Max(ms / 2, 60));
                    return;
                }

                // Raise moved cards above everything and animate.
                foreach (var card in moved)
                    Canvas.SetZIndex(_cardViews[card], 100 + moved.IndexOf(card));

                foreach (var card in moved)
                {
                    var (pile, idx) = Locate(card);
                    UpdateCardFace(card);
                    AnimateTo(_cardViews[card], CardPosition(pile, idx), ms);
                }
                await Task.Delay(ms + 20);
                LayoutAll(false);
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
            if (_autoPlaying || _animating) return;
            ClearSelection();
            var moves = _game.GetLegalMoves();
            var m = moves.FirstOrDefault(m => m.Kind is MoveKind.Draw or MoveKind.RecycleWaste);
            if (m is null) return;
            await AnimateMoveAsync(m);
        }

        private async void OnCardClicked(Card card)
        {
            if (_autoPlaying || _animating) return;
            var (pile, index) = Locate(card);

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
                CheckGameEnd(manual: true);
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
                CheckGameEnd(manual: true);
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
            if (_autoPlaying || _animating || !card.IsFaceUp) return;
            ClearSelection();
            var (pile, index) = Locate(card);
            if (index != pile.Count - 1) return;
            foreach (var f in _game.Foundations)
            {
                if (SolitaireGame.CanPlaceOnFoundation(card, f))
                {
                    var kind = pile.Type == PileType.Waste ? MoveKind.WasteToFoundation : MoveKind.TableauToFoundation;
                    await AnimateMoveAsync(new Move(kind, pile, f));
                    CheckGameEnd(manual: true);
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

        private async void CheckGameEnd(bool manual)
        {
            if (_game.IsWon)
            {
                _stats.RecordGameEnd(true, _game.MoveCount, DateTime.Now - _gameStart);
                _stats.Status = "🎉 You won! Press New Game.";
                await PlayWinCelebrationAsync();
            }
        }

        private async Task PlayWinCelebrationAsync()
        {
            // Burst every card outward with spin, then show the banner.
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
            if (_autoPlaying) { StopAutoPlay(); return; }

            _autoPlaying = true;
            AutoPlayButton.Content = "⏹  Stop";
            _stats.Status = "Computer is playing…";
            ClearSelection();

            try
            {
                while (_autoPlaying)
                {
                    _autoPlayer ??= new AutoPlayer(_game);
                    var move = _autoPlayer.ChooseMove();

                    if (move is null || _game.IsWon)
                    {
                        bool won = _game.IsWon;
                        _stats.RecordGameEnd(won, _game.MoveCount, DateTime.Now - _gameStart);
                        _stats.Status = won ? "🏆 Computer won! Dealing next game…" : "Stuck — dealing next game…";
                        if (won) await PlayWinCelebrationAsync();
                        await Task.Delay(won ? 800 : 900);
                        if (!_autoPlaying) break;

                        HideWinBanner();
                        _game = new SolitaireGame(drawCount: DrawMode);
                        _autoPlayer = new AutoPlayer(_game);
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

        private void StopAutoPlay() => _autoPlaying = false;

        #endregion

        #region Buttons

        private void NewGameButton_Click(object sender, RoutedEventArgs e) => StartNewGame();

        private void ResetStatsButton_Click(object sender, RoutedEventArgs e) => _stats.Reset();

        private void DrawModeChanged(object sender, RoutedEventArgs e)
        {
            if (_game is null || _autoPlaying) return;
            StartNewGame();
        }

        private async void HintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_autoPlaying || _animating) return;
            var player = new AutoPlayer(_game);
            var move = player.ChooseMove();
            if (move is null) { _stats.Status = "No moves available."; return; }
            _stats.Status = $"Hint: {Describe(move)}";
            await AnimateMoveAsync(move);
            CheckGameEnd(manual: true);
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
}