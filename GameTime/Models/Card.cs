namespace GameTime.Models;

public enum Suit { Clubs, Diamonds, Hearts, Spades }

public enum Rank
{
    Ace = 1, Two, Three, Four, Five, Six, Seven, Eight, Nine, Ten, Jack, Queen, King
}

public sealed class Card
{
    public Suit Suit { get; }
    public Rank Rank { get; }
    public bool IsFaceUp { get; set; }

    public Card(Suit suit, Rank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public bool IsRed => Suit is Suit.Diamonds or Suit.Hearts;

    public string ImageKey
    {
        get
        {
            string r = Rank switch
            {
                Rank.Ace => "A",
                Rank.Jack => "J",
                Rank.Queen => "Q",
                Rank.King => "K",
                _ => ((int)Rank).ToString()
            };
            string s = Suit switch
            {
                Suit.Clubs => "C",
                Suit.Diamonds => "D",
                Suit.Hearts => "H",
                _ => "S"
            };
            return r + s;
        }
    }

    public override string ToString() => $"{Rank} of {Suit}";
}
