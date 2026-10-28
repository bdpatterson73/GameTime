namespace GameTime.Models;

public enum MoveKind { Draw, RecycleWaste, WasteToTableau, WasteToFoundation, TableauToFoundation, TableauToTableau, FoundationToTableau, Flip }

public sealed record Move(MoveKind Kind, Pile? From = null, Pile? To = null, int CardCount = 1)
{
    public override string ToString() => $"{Kind} {From?.Type}{From?.Index} -> {To?.Type}{To?.Index} ({CardCount})";
}

public sealed class SolitaireGame
{
    public Pile Stock { get; } = new(PileType.Stock, 0);
    public Pile Waste { get; } = new(PileType.Waste, 0);
    public Pile[] Foundations { get; } = Enumerable.Range(0, 4).Select(i => new Pile(PileType.Foundation, i)).ToArray();
    public Pile[] Tableaus { get; } = Enumerable.Range(0, 7).Select(i => new Pile(PileType.Tableau, i)).ToArray();

    public int MoveCount { get; private set; }
    public int StockPasses { get; private set; }

    private readonly Random _random;

    public SolitaireGame(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        Deal();
    }

    public IEnumerable<Pile> AllPiles
    {
        get
        {
            yield return Stock;
            yield return Waste;
            foreach (var f in Foundations) yield return f;
            foreach (var t in Tableaus) yield return t;
        }
    }

    private void Deal()
    {
        var deck = new List<Card>();
        foreach (Suit s in Enum.GetValues<Suit>())
            foreach (Rank r in Enum.GetValues<Rank>())
                deck.Add(new Card(s, r));

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        int idx = 0;
        for (int col = 0; col < 7; col++)
        {
            for (int row = 0; row <= col; row++)
            {
                var card = deck[idx++];
                card.IsFaceUp = row == col;
                Tableaus[col].Cards.Add(card);
            }
        }
        while (idx < deck.Count)
        {
            var card = deck[idx++];
            card.IsFaceUp = false;
            Stock.Cards.Add(card);
        }
    }

    public bool IsWon => Foundations.All(f => f.Count == 13);

    public static bool CanPlaceOnTableau(Card card, Pile tableau)
    {
        var top = tableau.Top;
        if (top is null) return card.Rank == Rank.King;
        if (!top.IsFaceUp) return false;
        return top.IsRed != card.IsRed && (int)top.Rank == (int)card.Rank + 1;
    }

    public static bool CanPlaceOnFoundation(Card card, Pile foundation)
    {
        var top = foundation.Top;
        if (top is null) return card.Rank == Rank.Ace;
        return top.Suit == card.Suit && (int)top.Rank == (int)card.Rank - 1;
    }

    public List<Move> GetLegalMoves()
    {
        var moves = new List<Move>();

        // Tableau to foundation
        foreach (var t in Tableaus)
        {
            var top = t.Top;
            if (top is null || !top.IsFaceUp) continue;
            foreach (var f in Foundations)
                if (CanPlaceOnFoundation(top, f))
                    moves.Add(new Move(MoveKind.TableauToFoundation, t, f));
        }

        // Waste to foundation / tableau
        if (Waste.Top is { } w)
        {
            foreach (var f in Foundations)
                if (CanPlaceOnFoundation(w, f))
                    moves.Add(new Move(MoveKind.WasteToFoundation, Waste, f));
            foreach (var t in Tableaus)
                if (CanPlaceOnTableau(w, t))
                    moves.Add(new Move(MoveKind.WasteToTableau, Waste, t));
        }

        // Tableau to tableau (any face-up run)
        foreach (var from in Tableaus)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var card = from.Cards[i];
                if (!card.IsFaceUp) continue;
                foreach (var to in Tableaus)
                {
                    if (to == from) continue;
                    if (CanPlaceOnTableau(card, to))
                        moves.Add(new Move(MoveKind.TableauToTableau, from, to, from.Count - i));
                }
            }
        }

        if (Stock.Count > 0)
            moves.Add(new Move(MoveKind.Draw, Stock, Waste));
        else if (Waste.Count > 0)
            moves.Add(new Move(MoveKind.RecycleWaste, Waste, Stock));

        return moves;
    }

    /// <summary>Applies a move. Returns the cards that moved (in final order).</summary>
    public List<Card> ApplyMove(Move move)
    {
        MoveCount++;
        switch (move.Kind)
        {
            case MoveKind.Draw:
                {
                    var card = Stock.Cards[^1];
                    Stock.Cards.RemoveAt(Stock.Count - 1);
                    card.IsFaceUp = true;
                    Waste.Cards.Add(card);
                    return new List<Card> { card };
                }
            case MoveKind.RecycleWaste:
                {
                    StockPasses++;
                    var moved = new List<Card>(Waste.Cards);
                    moved.Reverse();
                    foreach (var c in moved) c.IsFaceUp = false;
                    Stock.Cards.Clear();
                    Stock.Cards.AddRange(moved);
                    Waste.Cards.Clear();
                    return moved;
                }
            default:
                {
                    var from = move.From!;
                    var to = move.To!;
                    int start = from.Count - move.CardCount;
                    var moved = from.Cards.GetRange(start, move.CardCount);
                    from.Cards.RemoveRange(start, move.CardCount);
                    to.Cards.AddRange(moved);
                    if (from.Top is { IsFaceUp: false } flip)
                        flip.IsFaceUp = true;
                    return moved;
                }
        }
    }
}
