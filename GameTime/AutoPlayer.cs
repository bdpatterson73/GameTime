using GameTime.Models;

namespace GameTime;

/// <summary>
/// Greedy heuristic Klondike player. Picks the best-scoring legal move,
/// avoids pointless repetition, and declares the game stuck after
/// unproductive full passes through the stock.
/// </summary>
public sealed class AutoPlayer
{
    private readonly SolitaireGame _game;
    private readonly HashSet<string> _seenStates = new();
    private int _movesSinceProgress;

    public AutoPlayer(SolitaireGame game) => _game = game;

    public bool IsStuck { get; private set; }

    public Move? ChooseMove()
    {
        if (IsStuck || _game.IsWon) return null;

        var moves = _game.GetLegalMoves();
        if (moves.Count == 0) { IsStuck = true; return null; }

        Move? best = null;
        int bestScore = int.MinValue;
        foreach (var m in moves)
        {
            int score = Score(m);
            if (score > bestScore) { bestScore = score; best = m; }
        }

        // Only draw/recycle available: track lack of progress across a full stock cycle.
        if (best!.Kind is MoveKind.Draw or MoveKind.RecycleWaste)
        {
            _movesSinceProgress++;
            if (_movesSinceProgress > _game.Stock.Count + _game.Waste.Count + 1)
            {
                IsStuck = true;
                return null;
            }
        }
        else
        {
            // Avoid infinite shuffling between tableaus: fingerprint the state.
            if (best.Kind == MoveKind.TableauToTableau)
            {
                string fp = Fingerprint(best);
                if (!_seenStates.Add(fp))
                {
                    // Skip repeated move; fall back to draw if possible.
                    var fallback = moves.FirstOrDefault(m => m.Kind is MoveKind.Draw or MoveKind.RecycleWaste);
                    if (fallback is null) { IsStuck = true; return null; }
                    _movesSinceProgress++;
                    if (_movesSinceProgress > _game.Stock.Count + _game.Waste.Count + 1) { IsStuck = true; return null; }
                    return fallback;
                }
            }
            _movesSinceProgress = 0;
        }

        return best;
    }

    private string Fingerprint(Move m)
    {
        var parts = _game.AllPiles.Select(p =>
            $"{p.Type}{p.Index}:" + string.Join(",", p.Cards.Select(c => c.ImageKey + (c.IsFaceUp ? "+" : "-"))));
        return string.Join("|", parts) + "::" + m;
    }

    private int Score(Move m)
    {
        switch (m.Kind)
        {
            case MoveKind.TableauToFoundation:
            case MoveKind.WasteToFoundation:
                return 100 + (int)(GetMovingCard(m)?.Rank ?? 0) * -1 + 20;
            case MoveKind.WasteToTableau:
                return 40;
            case MoveKind.TableauToTableau:
                {
                    var from = m.From!;
                    int start = from.Count - m.CardCount;
                    // Reward revealing a face-down card.
                    if (start > 0 && !from.Cards[start - 1].IsFaceUp)
                        return 60 + start; // deeper reveals are better
                    // Moving a full run that empties a column is good only if a King is waiting.
                    if (start == 0 && from.Cards[0].Rank != Rank.King)
                        return 10;
                    // Moving a King-led run from an empty-rooted column achieves nothing.
                    return -5;
                }
            case MoveKind.FoundationToTableau:
                return -20;
            case MoveKind.Draw:
                return 0;
            case MoveKind.RecycleWaste:
                return -10;
            default:
                return -100;
        }
    }

    private Card? GetMovingCard(Move m)
    {
        var from = m.From;
        if (from is null || from.Count < m.CardCount) return null;
        return from.Cards[from.Count - m.CardCount];
    }
}
