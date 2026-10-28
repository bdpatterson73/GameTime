using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameTime;

public sealed class GameStats : INotifyPropertyChanged
{
    private int _gamesPlayed;
    private int _gamesWon;
    private int _currentMoves;
    private int _bestWinMoves;
    private int _currentStreak;
    private int _bestStreak;
    private int _totalMoves;
    private int _cardsToFoundation;
    private TimeSpan _fastestWin;
    private string _status = "Ready";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name is nameof(GamesPlayed) or nameof(GamesWon))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WinRate)));
        }
    }

    public int GamesPlayed { get => _gamesPlayed; set => Set(ref _gamesPlayed, value); }
    public int GamesWon { get => _gamesWon; set => Set(ref _gamesWon, value); }
    public int CurrentMoves { get => _currentMoves; set => Set(ref _currentMoves, value); }
    public int BestWinMoves { get => _bestWinMoves; set => Set(ref _bestWinMoves, value); }
    public int CurrentStreak { get => _currentStreak; set => Set(ref _currentStreak, value); }
    public int BestStreak { get => _bestStreak; set => Set(ref _bestStreak, value); }
    public int TotalMoves { get => _totalMoves; set => Set(ref _totalMoves, value); }
    public int CardsToFoundation { get => _cardsToFoundation; set => Set(ref _cardsToFoundation, value); }
    public TimeSpan FastestWin { get => _fastestWin; set { Set(ref _fastestWin, value); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FastestWinText))); } }
    public string Status { get => _status; set => Set(ref _status, value); }

    public string WinRate => GamesPlayed == 0 ? "—" : $"{(double)GamesWon / GamesPlayed:P1}";
    public string FastestWinText => FastestWin == TimeSpan.Zero ? "—" : $"{FastestWin:mm\\:ss}";

    public void RecordGameEnd(bool won, int moves, TimeSpan duration)
    {
        GamesPlayed++;
        TotalMoves += moves;
        if (won)
        {
            GamesWon++;
            CurrentStreak++;
            if (CurrentStreak > BestStreak) BestStreak = CurrentStreak;
            if (BestWinMoves == 0 || moves < BestWinMoves) BestWinMoves = moves;
            if (FastestWin == TimeSpan.Zero || duration < FastestWin) FastestWin = duration;
        }
        else
        {
            CurrentStreak = 0;
        }
    }
}
