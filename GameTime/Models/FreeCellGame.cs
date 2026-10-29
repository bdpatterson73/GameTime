namespace GameTime.Models;

public enum FcMoveKind { CascadeToFoundation, CascadeToCell, CascadeToCascade, CellToFoundation, CellToCascade }

public sealed record FcMove(FcMoveKind Kind, Pile From, Pile To, int CardCount = 1)
{
    public override string ToString() => $"{Kind} {From.Type}{From.Index} -> {To.Type}{To.Index} ({CardCount})";
}

/// <summary>
/// FreeCell: 8 cascades (all cards face up), 4 free cells, 4 foundations.
/// Supermoves are limited by (free cells + 1) * (empty cascades + 1).
/// Free cells and cascades are modeled as Piles; free cells hold at most one card.
/// </summary>
public sealed class FreeCellGame
{
    public Pile[] Cells { get; } = Enumerable.Range(0, 4).Select(i => new Pile(PileType.Stock, i)).ToArray();
    public Pile[] Foundations { get; } = Enumerable.Range(0, 4).Select(i => new Pile(PileType.Foundation, i)).ToArray();
    public Pile[] Cascades { get; } = Enumerable.Range(0, 8).Select(i => new Pile(PileType.Tableau, i)).ToArray();

    public int MoveCount { get; private set; }

    public FreeCellGame(int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var deck = new List<Card>();
        foreach (Suit s in Enum.GetValues<Suit>())
            foreach (Rank r in Enum.GetValues<Rank>())
                deck.Add(new Card(s, r) { IsFaceUp = true });

        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }

        for (int i = 0; i < deck.Count; i++)
            Cascades[i % 8].Cards.Add(deck[i]);
    }

    public IEnumerable<Pile> AllPiles
    {
        get
        {
            foreach (var c in Cells) yield return c;
            foreach (var f in Foundations) yield return f;
            foreach (var t in Cascades) yield return t;
        }
    }

    public bool IsWon => Foundations.All(f => f.Count == 13);

    public int FreeCellCount => Cells.Count(c => c.Count == 0);
    public int EmptyCascadeCount => Cascades.Count(c => c.Count == 0);

    /// <summary>Max cards movable as a unit when the target cascade is non-empty.</summary>
    public int MaxSupermove => (FreeCellCount + 1) * (EmptyCascadeCount + 1);

    public static bool CanPlaceOnCascade(Card card, Pile cascade)
    {
        var top = cascade.Top;
        if (top is null) return true;
        return top.IsRed != card.IsRed && (int)top.Rank == (int)card.Rank + 1;
    }

    public static bool CanPlaceOnFoundation(Card card, Pile foundation)
    {
        var top = foundation.Top;
        if (top is null) return card.Rank == Rank.Ace;
        return top.Suit == card.Suit && (int)top.Rank == (int)card.Rank - 1;
    }

    /// <summary>Length of the valid alternating descending run ending at the top of a cascade.</summary>
    public static int RunLength(Pile cascade)
    {
        int len = 0;
        for (int i = cascade.Count - 1; i >= 0; i--)
        {
            len++;
            if (i == 0) break;
            var upper = cascade.Cards[i - 1];
            var lower = cascade.Cards[i];
            if (upper.IsRed == lower.IsRed || (int)upper.Rank != (int)lower.Rank + 1) break;
        }
        return len;
    }

    public List<FcMove> GetLegalMoves()
    {
        var moves = new List<FcMove>();

        // From cells
        foreach (var cell in Cells)
        {
            if (cell.Top is not { } c) continue;
            foreach (var f in Foundations)
                if (CanPlaceOnFoundation(c, f))
                    moves.Add(new FcMove(FcMoveKind.CellToFoundation, cell, f));
            foreach (var t in Cascades)
                if (CanPlaceOnCascade(c, t))
                    moves.Add(new FcMove(FcMoveKind.CellToCascade, cell, t));
        }

        // From cascades
        foreach (var from in Cascades)
        {
            if (from.Top is not { } top) continue;

            foreach (var f in Foundations)
                if (CanPlaceOnFoundation(top, f))
                    moves.Add(new FcMove(FcMoveKind.CascadeToFoundation, from, f));

            if (FreeCellCount > 0)
            {
                var cell = Cells.First(c => c.Count == 0);
                moves.Add(new FcMove(FcMoveKind.CascadeToCell, from, cell));
            }

            int run = RunLength(from);
            foreach (var to in Cascades)
            {
                if (to == from) continue;
                // Moving to an empty cascade halves the multiplier for that cascade.
                int limit = to.Count == 0
                    ? (FreeCellCount + 1) * EmptyCascadeCount
                    : MaxSupermove;
                for (int n = 1; n <= Math.Min(run, limit); n++)
                {
                    var head = from.Cards[from.Count - n];
                    if (CanPlaceOnCascade(head, to))
                        moves.Add(new FcMove(FcMoveKind.CascadeToCascade, from, to, n));
                }
            }
        }

        return moves;
    }

    public List<Card> ApplyMove(FcMove move)
    {
        MoveCount++;
        int start = move.From.Count - move.CardCount;
        var moved = move.From.Cards.GetRange(start, move.CardCount);
        move.From.Cards.RemoveRange(start, move.CardCount);
        move.To.Cards.AddRange(moved);
        return moved;
    }
}
