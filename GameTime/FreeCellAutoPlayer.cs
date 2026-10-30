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
    private readonly Dictionary<Card, Pile> _cameFrom = new();

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
            {
                _cameFrom[move.From.Cards[move.From.Count - move.CardCount]] = move.From;
                return move;
            }
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

        // Moving a card straight back where it just came from is never useful:
        // every card in FreeCell is already visible, nothing new is learned.
        bool movingBack = _cameFrom.TryGetValue(moving, out var prev) && prev == m.To;

        switch (m.Kind)
        {
            case FcMoveKind.CascadeToFoundation:
            case FcMoveKind.CellToFoundation:
                return IsSafeFoundationMove(moving) ? 200 : 90;

            case FcMoveKind.CellToCascade:
                // Emptying a free cell back onto a run is good, unless it just left there.
                return movingBack ? -1500 : 70;

            case FcMoveKind.CascadeToCascade:
                {
                    if (movingBack) return -1500;

                    int start = m.From.Count - m.CardCount;
                    bool emptiesColumn = start == 0;
                    bool targetEmpty = m.To.Count == 0;

                    // Shuttling full runs between empty columns is pointless.
                    if (emptiesColumn && targetEmpty) return -2000;

                    // Does it uncover a card that can go to a foundation?
                    bool exposesFoundationCard = false;
                    if (start > 0)
                    {
                        var exposed = m.From.Cards[start - 1];
                        exposesFoundationCard = _game.Foundations.Any(f => FreeCellGame.CanPlaceOnFoundation(exposed, f));
                    }

                    // Parking on an empty column must accomplish something concrete;
                    // all cards are visible, so moving "to see" the card below is useless.
                    if (targetEmpty && !exposesFoundationCard)
                        return -1200;

                    int score = 30 + m.CardCount; // bigger consolidations preferred
                    if (emptiesColumn) score += 25; // frees a column
                    if (!targetEmpty) score += 10; // builds a real run
                    else score -= 15; // parking on an empty column is a last resort
                    if (exposesFoundationCard) score += 60;
                    return score;
                }

            case FcMoveKind.CascadeToCell:
                {
                    if (movingBack) return -1500;
                    // Burning a free cell is a last resort; worse the fewer cells remain.
                    int score = -40 + _game.FreeCellCount * 8;
                    // Only worthwhile if it exposes a foundation-playable card.
                    if (m.From.Count > 1)
                    {
                        var exposed = m.From.Cards[m.From.Count - 2];
                        if (_game.Foundations.Any(f => FreeCellGame.CanPlaceOnFoundation(exposed, f)))
                            score += 80;
                    }
                    return score;
                }

            default:
                return -3000;
        }
    }
}
