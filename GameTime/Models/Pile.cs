namespace GameTime.Models;

public enum PileType { Stock, Waste, Foundation, Tableau }

public sealed class Pile
{
    public PileType Type { get; }
    public int Index { get; }
    public List<Card> Cards { get; } = new();

    public Pile(PileType type, int index)
    {
        Type = type;
        Index = index;
    }

    public Card? Top => Cards.Count > 0 ? Cards[^1] : null;
    public int Count => Cards.Count;
}
