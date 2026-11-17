using System.Runtime.InteropServices;
using GameTime.WinUI.Pages;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.System;

namespace GameTime.WinUI;

/// <summary>
/// Single-window shell. Games are pages hosted in one Frame, so switching between
/// the hub and a game is an in-window composition transition instead of a new window.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<Type, string> PageTitles = new()
    {
        [typeof(KlondikePage)] = "Klondike",
        [typeof(FreeCellPage)] = "FreeCell",
        [typeof(MinesweeperPage)] = "Minesweeper",
        [typeof(ShooterPage)] = "Maze Blaster"
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SizeAndCenter(1320, 900);

        // The XAML parser can't convert "GoBack" to a VirtualKey, so this accelerator is added here.
        var browserBack = new KeyboardAccelerator { Key = VirtualKey.GoBack };
        browserBack.Invoked += Back_Invoked;
        RootGrid.KeyboardAccelerators.Add(browserBack);

        RootFrame.Navigate(typeof(HubPage), null, new SuppressNavigationTransitionInfo());
    }

    private void SizeAndCenter(int width, int height)
    {
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Min((int)(width * scale), area.Width);
        int h = Math.Min((int)(height * scale), area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    private void GoBack()
    {
        if (RootFrame.CanGoBack) RootFrame.GoBack();
    }

    private void RootFrame_Navigated(object sender, NavigationEventArgs e)
    {
        AppTitleBar.IsBackButtonVisible = RootFrame.CanGoBack;
        AppTitleBar.Subtitle = PageTitles.GetValueOrDefault(e.SourcePageType, string.Empty);
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args) => GoBack();

    private void Back_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = RootFrame.CanGoBack;
        GoBack();
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootGrid).Properties.IsXButton1Pressed) return;
        e.Handled = true;
        GoBack();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
