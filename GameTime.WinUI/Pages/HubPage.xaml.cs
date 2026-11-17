using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace GameTime.WinUI.Pages;

public sealed partial class HubPage : Page
{
    private const float RestElevation = 16f;
    private const float HoverElevation = 48f;
    private const float HoverScale = 1.04f;
    private const float PressScale = 0.97f;

    private UIElement? _openedIcon;

    public HubPage()
    {
        InitializeComponent();

        foreach (var tile in TilePanel.Children.OfType<Button>())
        {
            // Hover lifts the tile (deeper shadow) and springs it larger; pressing springs it back in.
            // Scale is only ever driven by springs, so quick pointer changes blend instead of restarting.
            tile.Shadow = new ThemeShadow();
            tile.Translation = new Vector3(0, 0, RestElevation);
            tile.TranslationTransition = Motion.Enabled ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(140) } : null;
            tile.CenterPoint = new Vector3((float)tile.Width / 2, (float)tile.Height / 2, 0);
            tile.PointerEntered += (_, _) => SetLifted(tile, true);
            tile.PointerExited += (_, _) => SetLifted(tile, false);
            tile.PointerCanceled += (_, _) => SetLifted(tile, false);
            tile.PointerCaptureLost += (_, _) => SetLifted(tile, false);
            // Button marks press/release handled, so listen for handled events too.
            tile.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => Motion.SpringScale(tile, PressScale)), true);
            tile.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => Motion.SpringScale(tile, HoverScale)), true);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.NavigationMode == NavigationMode.Back && _openedIcon is not null)
            Motion.PlayReturnAnimation(_openedIcon);
    }

    private static void SetLifted(Button tile, bool lifted)
    {
        Motion.SpringScale(tile, lifted ? HoverScale : 1f);
        tile.Translation = new Vector3(0, 0, lifted ? HoverElevation : RestElevation);
    }

    private void Open(Type page, UIElement icon)
    {
        // This page is cached, and pointer-exit never arrives once it leaves the tree,
        // so drop every tile back to rest before navigating away.
        foreach (var tile in TilePanel.Children.OfType<Button>())
            SetLifted(tile, false);
        _openedIcon = icon;
        Motion.PrepareHeaderAnimation(icon);
        Frame.Navigate(page, null, new DrillInNavigationTransitionInfo());
    }

    private void Klondike_Click(object sender, RoutedEventArgs e) => Open(typeof(KlondikePage), KlondikeIcon);
    private void FreeCell_Click(object sender, RoutedEventArgs e) => Open(typeof(FreeCellPage), FreeCellIcon);
    private void Minesweeper_Click(object sender, RoutedEventArgs e) => Open(typeof(MinesweeperPage), MinesweeperIcon);
    private void Shooter_Click(object sender, RoutedEventArgs e) => Open(typeof(ShooterPage), ShooterIcon);
}
