using GameTime.Models;

namespace GameTime;

/// <summary>
/// Heuristic FreeCell player. Prefers safe foundation moves, run-building
/// cascade moves, and uses free cells sparingly. Detects loops via state
/// fingerprints and gives up when no fresh productive move exists.
/// </summary>
public sealed class FreeCellAutoPlayer
{
    private readonly FreeCellGame _game;
    private readonly HashSet<string> _seenStates = new();

    public FreeCellAutoPlayer(FreeCellGame game) => _game = game;

    public bool IsStuck { get; private set; }

    public FcMove? ChooseMove()
    {
        if (IsStuck || _game.IsWon) return null;

        var moves = _game.GetLegalMoves();
        if (moves.Count == 0) { IsStuck = true; return null; }

        foreach (var move in moves.OrderByDescending(Score))
        {
            if (Score(move) <= -1000) break; // only hopeless moves left
            string fp = Fingerprint(move);
            if (_seenStates.Add(fp))
                return move;
        }

        IsStuck = true;
        return null;
    }

    private string Fingerprint(FcMove m)
    {
        var parts = _game.AllPiles.Select(p =>
            $"{p.Type}{p.Index}:" + string.Join(",", p.Cards.Select(c => c.ImageKey)));
        return string.Join("|", parts) + "::" + m;
    }

    /// <summary>A foundation move is safe if both opposite-color foundations are high enough.</summary>
    private bool IsSafeFoundationMove(Card card)
    {
        if (card.Rank is Rank.Ace or Rank.Two) return true;
        int minOpposite = _game.Foundations
            .Where(f => f.Top is null || f.Top.IsRed != card.IsRed)
            .Select(f => f.Top is null ? 0 : (int)f.Top.Rank)
            .DefaultIfEmpty(0)
            .Min();
        return minOpposite >= (int)card.Rank - 1;
    }

    private int Score(FcMove m)
    {
        var moving = m.From.Cards[m.From.Count - m.CardCount];
        switch (m.Kind)
        {
            case FcMoveKind.CascadeToFoundation:
            case FcMoveKind.CellToFoundation:
                return IsSafeFoundationMove(moving) ? 200 : 90;

            case FcMoveKind.CellToCascade:
                // Emptying a free cell back onto a run is good.
                return 70;

            case FcMoveKind.CascadeToCascade:
                {
                    int start = m.From.Count - m.CardCount;
                    bool emptiesColumn = start == 0;
                    bool targetEmpty = m.To.Count == 0;

                    // Shuttling full runs between empty columns is pointless.
                    if (emptiesColumn && targetEmpty) return -2000;

                    int score = 30 + m.CardCount; // bigger consolidations preferred
                    if (emptiesColumn) score += 25; // frees a column
                    if (!targetEmpty) score += 10; // builds a real run
                    else score -= 15; // parking on an empty column is a last resort
                    // Does it uncover a card that can go to a foundation?
                    if (start > 0)
                    {
                        var exposed = m.From.Cards[start - 1];
                        if (_game.Foundations.Any(f => FreeCellGame.CanPlaceOnFoundation(exposed, f)))
                            score += 60;
                    }
                    return score;
                }

            case FcMoveKind.CascadeToCell:
                // Burning a free cell is a last resort; worse the fewer cells remain.
                return -40 + _game.FreeCellCount * 8;

            default:
                return -3000;
        }
    }
}
