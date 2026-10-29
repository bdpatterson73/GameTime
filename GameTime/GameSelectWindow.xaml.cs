using System.Windows;

namespace GameTime
{
    public partial class GameSelectWindow : Window
    {
        public GameSelectWindow()
        {
            InitializeComponent();
        }

        private void Launch(Window game)
        {
            Hide();
            game.Owner = null;
            game.ShowDialog();
            Show();
        }

        private void Solitaire_Click(object sender, RoutedEventArgs e) => Launch(new MainWindow());
        private void FreeCell_Click(object sender, RoutedEventArgs e) => Launch(new FreeCellWindow());
        private void Minesweeper_Click(object sender, RoutedEventArgs e) => Launch(new MinesweeperWindow());
    }
}
